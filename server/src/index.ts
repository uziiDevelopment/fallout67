import { DurableObject } from 'cloudflare:workers';

export interface Env {
	GAME_ROOMS: DurableObjectNamespace;
	LEADERBOARD: DurableObjectNamespace;
	PLAYER_PROFILES: DurableObjectNamespace;
}

interface Player {
	id: string;
	name: string;
	country: string | null;
	color: string;
}

interface RoomState {
	code: string;
	hostId: string;
	players: Player[];
	gameStarted: boolean;
	seed: number;
	takenCountries: string[];
	diplomacyLog: DiplomacyEvent[];
}

interface DiplomacyEvent {
	action: unknown; // the full game_action payload relayed to clients
}

const PLAYER_COLORS = ['#00FFFF', '#FF6600', '#FF00FF', '#FFFF00', '#00FF99', '#FF4444'];

function generateCode(): string {
	const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
	let code = '';
	for (let i = 0; i < 6; i++) code += chars[Math.floor(Math.random() * chars.length)];
	return code;
}

// ── Durable Object: one instance per room code ──────────────────────────────
export class GameRoom extends DurableObject {
	constructor(ctx: DurableObjectState, env: Env) {
		super(ctx, env);
	}

	async fetch(request: Request): Promise<Response> {
		if (request.headers.get('Upgrade')?.toLowerCase() !== 'websocket') {
			return new Response('Expected WebSocket upgrade', { status: 426 });
		}

		const url = new URL(request.url);
		const playerName = (url.searchParams.get('name') || 'Commander').substring(0, 24);
		const code = url.searchParams.get('code') || 'ROOM';

		// Load or initialise room state
		let room = await this.ctx.storage.get<RoomState>('room');
		if (!room) {
			room = {
				code,
				hostId: '',
				players: [],
				gameStarted: false,
				seed: Math.floor(Math.random() * 1_000_000),
				takenCountries: [],
				diplomacyLog: [],
			};
		}

		// Ensure diplomacyLog exists for rooms created before this field was added
		if (!room.diplomacyLog) room.diplomacyLog = [];

		let playerId: string;
		let color: string;
		let isReconnect = false;

		if (room.gameStarted) {
			// Mid-game: only allow reconnection by matching player name
			const existing = room.players.find((p) => p.name === playerName);
			if (!existing) {
				// Truly new player trying to join mid-game — reject
				const [client, server] = Object.values(new WebSocketPair()) as [WebSocket, WebSocket];
				this.ctx.acceptWebSocket(server);
				server.send(JSON.stringify({ type: 'error', message: 'Game already in progress' }));
				server.close(1008, 'Game in progress');
				return new Response(null, { status: 101, webSocket: client });
			}
			// Reconnecting player — reuse their identity
			playerId = existing.id;
			color = existing.color;
			isReconnect = true;
		} else {
			// Pre-game: new player joins
			playerId = crypto.randomUUID();
			color = PLAYER_COLORS[room.players.length % PLAYER_COLORS.length];
			if (!room.hostId) room.hostId = playerId;

			const player: Player = { id: playerId, name: playerName, country: null, color };
			room.players.push(player);
			await this.ctx.storage.put('room', room);
		}

		const [client, server] = Object.values(new WebSocketPair()) as [WebSocket, WebSocket];
		this.ctx.acceptWebSocket(server);
		server.serializeAttachment({ playerId, name: playerName });

		// Greet player with full room state
		server.send(
			JSON.stringify({
				type: 'welcome',
				playerId,
				isHost: playerId === room.hostId,
				room,
				isReconnect,
			}),
		);

		if (isReconnect) {
			// Replay diplomacy events so the reconnecting client catches up
			for (const evt of room.diplomacyLog) {
				server.send(JSON.stringify({ type: 'game_action', senderId: 'server', action: evt.action }));
			}
			// Tell others the player is back
			this.broadcast(server, { type: 'player_reconnected', playerId, name: playerName, players: room.players });
		} else {
			// Tell everyone else about the new player
			const player = room.players.find((p) => p.id === playerId)!;
			this.broadcast(server, { type: 'player_joined', player, players: room.players });
		}

		return new Response(null, { status: 101, webSocket: client });
	}

	async webSocketMessage(ws: WebSocket, message: string | ArrayBuffer): Promise<void> {
		const meta = ws.deserializeAttachment() as { playerId: string } | null;
		if (!meta) return;

		let room = await this.ctx.storage.get<RoomState>('room');
		if (!room) return;

		const player = room.players.find((p) => p.id === meta.playerId);
		if (!player) return;

		let msg: { type: string; [k: string]: unknown };
		try {
			msg = JSON.parse(message as string);
		} catch {
			return;
		}

		switch (msg.type) {
			case 'select_country': {
				if (room.gameStarted) break;
				const country = String(msg.country ?? '');
				if (room.takenCountries.includes(country)) {
					ws.send(JSON.stringify({ type: 'error', message: `${country} is already taken` }));
					break;
				}
				// Free previous selection
				if (player.country) room.takenCountries = room.takenCountries.filter((c) => c !== player.country);
				player.country = country;
				room.takenCountries.push(country);
				await this.ctx.storage.put('room', room);
				this.broadcastAll({ type: 'country_selected', playerId: meta.playerId, country, takenCountries: room.takenCountries, players: room.players });
				break;
			}

			case 'start_game': {
				if (meta.playerId !== room.hostId || room.gameStarted) break;
				const eligible = room.players.filter((p) => p.country !== null);
				if (eligible.length === 0) {
					ws.send(JSON.stringify({ type: 'error', message: 'Select a country first' }));
					break;
				}
				room.gameStarted = true;
				await this.ctx.storage.put('room', room);
				this.broadcastAll({ type: 'game_start', seed: room.seed, players: room.players });
				break;
			}

			case 'game_action': {
				if (!room.gameStarted) break;
				// Record diplomacy events for replay on reconnect
				const action = msg.action as { type?: string } | undefined;
				if (action?.type === 'diplomacy_alliance' || action?.type === 'diplomacy_betrayal') {
					room.diplomacyLog.push({ action });
					// Cap log size to prevent unbounded growth
					if (room.diplomacyLog.length > 200) room.diplomacyLog = room.diplomacyLog.slice(-200);
					await this.ctx.storage.put('room', room);
				}
				// Relay to everyone else; they apply it locally
				this.broadcast(ws, { type: 'game_action', senderId: meta.playerId, action: msg.action });
				break;
			}

			case 'chat': {
				const text = String(msg.text ?? '').substring(0, 200);
				this.broadcastAll({ type: 'chat', senderId: meta.playerId, name: player.name, text });
				break;
			}
		}
	}

	async webSocketClose(ws: WebSocket): Promise<void> {
		const meta = ws.deserializeAttachment() as { playerId: string } | null;
		if (!meta) return;

		let room = await this.ctx.storage.get<RoomState>('room');
		if (!room) return;

		if (room.gameStarted) {
			// Mid-game: keep the player in the roster so they can reconnect.
			// Only transfer host if needed and notify others of temporary disconnect.
			if (room.hostId === meta.playerId) {
				// Find another *connected* player to be host
				const connectedIds = new Set<string>();
				for (const sock of this.ctx.getWebSockets()) {
					if (sock === ws) continue;
					const m = sock.deserializeAttachment() as { playerId: string } | null;
					if (m) connectedIds.add(m.playerId);
				}
				const newHost = room.players.find((p) => connectedIds.has(p.id));
				if (newHost) {
					room.hostId = newHost.id;
					await this.ctx.storage.put('room', room);
					this.sendToPlayer(newHost.id, { type: 'you_are_host' });
				}
			}
			this.broadcastAll({ type: 'player_disconnected', playerId: meta.playerId, name: room.players.find(p => p.id === meta.playerId)?.name, players: room.players });
		} else {
			// Pre-game: fully remove the player
			const leaving = room.players.find((p) => p.id === meta.playerId);
			if (leaving?.country) room.takenCountries = room.takenCountries.filter((c) => c !== leaving.country);
			room.players = room.players.filter((p) => p.id !== meta.playerId);

			if (room.hostId === meta.playerId && room.players.length > 0) {
				room.hostId = room.players[0].id;
				this.sendToPlayer(room.players[0].id, { type: 'you_are_host' });
			}

			await this.ctx.storage.put('room', room);
			this.broadcastAll({ type: 'player_left', playerId: meta.playerId, players: room.players });
		}
	}

	private broadcast(exclude: WebSocket, msg: object): void {
		const json = JSON.stringify(msg);
		for (const ws of this.ctx.getWebSockets()) {
			if (ws !== exclude) try { ws.send(json); } catch { /* ignore closed */ }
		}
	}

	private broadcastAll(msg: object): void {
		const json = JSON.stringify(msg);
		for (const ws of this.ctx.getWebSockets()) {
			try { ws.send(json); } catch { /* ignore closed */ }
		}
	}

	private sendToPlayer(playerId: string, msg: object): void {
		const json = JSON.stringify(msg);
		for (const ws of this.ctx.getWebSockets()) {
			const m = ws.deserializeAttachment() as { playerId: string } | null;
			if (m?.playerId === playerId) { try { ws.send(json); } catch { } break; }
		}
	}
}

// ── Leaderboard Durable Object ────────────────────────────────────────────────
interface ScoreEntry {
	name: string;
	nation: string;
	score: number;
	seconds: number;
	nukesUsed: number;
	date: string;
}

export class Leaderboard extends DurableObject {
	async fetch(request: Request): Promise<Response> {
		const cors = {
			'Access-Control-Allow-Origin': '*',
			'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
			'Access-Control-Allow-Headers': 'Content-Type',
		};

		if (request.method === 'OPTIONS') return new Response(null, { headers: cors });

		if (request.method === 'POST') {
			let body: { name?: string; nation?: string; score?: number; seconds?: number; nukesUsed?: number };
			try { body = await request.json(); } catch { return new Response('Bad JSON', { status: 400 }); }

			const entry: ScoreEntry = {
				name: String(body.name ?? 'Commander').substring(0, 24),
				nation: String(body.nation ?? 'Unknown').substring(0, 32),
				score: Math.max(0, Math.floor(Number(body.score) || 0)),
				seconds: Math.max(0, Math.floor(Number(body.seconds) || 0)),
				nukesUsed: Math.max(0, Math.floor(Number(body.nukesUsed) || 0)),
				date: new Date().toISOString(),
			};

			const scores: ScoreEntry[] = (await this.ctx.storage.get<ScoreEntry[]>('scores')) ?? [];
			scores.push(entry);
			scores.sort((a, b) => b.score - a.score);
			scores.splice(20); // keep top 20
			await this.ctx.storage.put('scores', scores);

			return new Response(JSON.stringify({ ok: true, rank: scores.findIndex(s => s === entry) + 1 }), {
				headers: { ...cors, 'Content-Type': 'application/json' },
			});
		}

		// GET — return the leaderboard
		const scores: ScoreEntry[] = (await this.ctx.storage.get<ScoreEntry[]>('scores')) ?? [];
		return new Response(JSON.stringify({ scores }), {
			headers: { ...cors, 'Content-Type': 'application/json' },
		});
	}
}

// ── Player Profiles Durable Object ────────────────────────────────────────────
interface PlayerProfileData {
	Username: string;
	CreatedAt: string;
	LastPlayed: string;
	MatchesPlayed: number;
	MatchesWon: number;
	MatchesLost: number;
	TotalKills: number;
	TotalNukesLaunched: number;
	StandardNukesLaunched: number;
	TsarBombasLaunched: number;
	BioPlaguesLaunched: number;
	OrbitalLasersFired: number;
	SatelliteKillersUsed: number;
	NationsConquered: number;
	NationsSurrendered: number;
	MissilesIntercepted: number;
	DamageAbsorbed: number;
	TroopMissionsLaunched: number;
	TroopMissionsSucceeded: number;
	TroopMissionsFailed: number;
	AlliancesFormed: number;
	AlliancesBroken: number;
	SubmarinesDeployed: number;
	SubmarineStrikesFired: number;
	SubmarinesLost: number;
	HighestScore: number;
	TotalScoreEarned: number;
	TotalPlayTimeSeconds: number;
	LongestGameSeconds: number;
	ShortestVictorySeconds: number;
	MultiplayerGamesPlayed: number;
	MultiplayerWins: number;
	NationPlayCounts: Record<string, number>;
}

export class PlayerProfiles extends DurableObject {
	async fetch(request: Request): Promise<Response> {
		const cors = {
			'Access-Control-Allow-Origin': '*',
			'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
			'Access-Control-Allow-Headers': 'Content-Type',
		};

		if (request.method === 'OPTIONS') return new Response(null, { headers: cors });

		const url = new URL(request.url);

		if (request.method === 'POST') {
			// Upsert a profile
			let body: Partial<PlayerProfileData>;
			try { body = await request.json(); } catch { return new Response('Bad JSON', { status: 400, headers: cors }); }

			const username = String(body.Username ?? '').trim().substring(0, 24);
			if (!username) return new Response('Missing Username', { status: 400, headers: cors });

			const key = `profile:${username.toLowerCase()}`;
			const profile: PlayerProfileData = {
				Username: username,
				CreatedAt: body.CreatedAt ?? new Date().toISOString(),
				LastPlayed: body.LastPlayed ?? new Date().toISOString(),
				MatchesPlayed: Math.max(0, Number(body.MatchesPlayed) || 0),
				MatchesWon: Math.max(0, Number(body.MatchesWon) || 0),
				MatchesLost: Math.max(0, Number(body.MatchesLost) || 0),
				TotalKills: Math.max(0, Number(body.TotalKills) || 0),
				TotalNukesLaunched: Math.max(0, Number(body.TotalNukesLaunched) || 0),
				StandardNukesLaunched: Math.max(0, Number(body.StandardNukesLaunched) || 0),
				TsarBombasLaunched: Math.max(0, Number(body.TsarBombasLaunched) || 0),
				BioPlaguesLaunched: Math.max(0, Number(body.BioPlaguesLaunched) || 0),
				OrbitalLasersFired: Math.max(0, Number(body.OrbitalLasersFired) || 0),
				SatelliteKillersUsed: Math.max(0, Number(body.SatelliteKillersUsed) || 0),
				NationsConquered: Math.max(0, Number(body.NationsConquered) || 0),
				NationsSurrendered: Math.max(0, Number(body.NationsSurrendered) || 0),
				MissilesIntercepted: Math.max(0, Number(body.MissilesIntercepted) || 0),
				DamageAbsorbed: Math.max(0, Number(body.DamageAbsorbed) || 0),
				TroopMissionsLaunched: Math.max(0, Number(body.TroopMissionsLaunched) || 0),
				TroopMissionsSucceeded: Math.max(0, Number(body.TroopMissionsSucceeded) || 0),
				TroopMissionsFailed: Math.max(0, Number(body.TroopMissionsFailed) || 0),
				AlliancesFormed: Math.max(0, Number(body.AlliancesFormed) || 0),
				AlliancesBroken: Math.max(0, Number(body.AlliancesBroken) || 0),
				SubmarinesDeployed: Math.max(0, Number(body.SubmarinesDeployed) || 0),
				SubmarineStrikesFired: Math.max(0, Number(body.SubmarineStrikesFired) || 0),
				SubmarinesLost: Math.max(0, Number(body.SubmarinesLost) || 0),
				HighestScore: Math.max(0, Number(body.HighestScore) || 0),
				TotalScoreEarned: Math.max(0, Number(body.TotalScoreEarned) || 0),
				TotalPlayTimeSeconds: Math.max(0, Number(body.TotalPlayTimeSeconds) || 0),
				LongestGameSeconds: Math.max(0, Number(body.LongestGameSeconds) || 0),
				ShortestVictorySeconds: Number(body.ShortestVictorySeconds) || 2147483647,
				MultiplayerGamesPlayed: Math.max(0, Number(body.MultiplayerGamesPlayed) || 0),
				MultiplayerWins: Math.max(0, Number(body.MultiplayerWins) || 0),
				NationPlayCounts: (body.NationPlayCounts && typeof body.NationPlayCounts === 'object') ? body.NationPlayCounts : {},
			};

			await this.ctx.storage.put(key, profile);

			return new Response(JSON.stringify({ ok: true }), {
				headers: { ...cors, 'Content-Type': 'application/json' },
			});
		}

		// GET — fetch a profile by name
		const name = url.searchParams.get('name');
		if (name) {
			const key = `profile:${name.toLowerCase().trim()}`;
			const profile = await this.ctx.storage.get<PlayerProfileData>(key);

			if (!profile) {
				return new Response(JSON.stringify({ error: 'Profile not found' }), {
					status: 404,
					headers: { ...cors, 'Content-Type': 'application/json' },
				});
			}

			return new Response(JSON.stringify(profile), {
				headers: { ...cors, 'Content-Type': 'application/json' },
			});
		}

		// GET without ?name — list all profiles (for /api/profiles)
		const allEntries = await this.ctx.storage.list<PlayerProfileData>({ prefix: 'profile:' });
		const profiles: PlayerProfileData[] = [];
		for (const [, value] of allEntries) {
			profiles.push(value);
		}
		// Sort by highest score descending
		profiles.sort((a, b) => b.HighestScore - a.HighestScore);
		// Cap at 100 profiles to avoid huge payloads
		const capped = profiles.slice(0, 100);

		return new Response(JSON.stringify({ Profiles: capped }), {
			headers: { ...cors, 'Content-Type': 'application/json' },
		});
	}
}

// ── Main Worker ──────────────────────────────────────────────────────────────
export default {
	async fetch(request: Request, env: Env): Promise<Response> {
		const url = new URL(request.url);
		const cors = {
			'Access-Control-Allow-Origin': '*',
			'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
			'Access-Control-Allow-Headers': 'Content-Type',
		};

		if (request.method === 'OPTIONS') return new Response(null, { headers: cors });

		// Leaderboard endpoints — routed to the singleton Leaderboard DO
		if (url.pathname === '/api/score' || url.pathname === '/api/leaderboard') {
			const id = env.LEADERBOARD.idFromName('global');
			return env.LEADERBOARD.get(id).fetch(request);
		}

		// Player profile endpoints — routed to the singleton PlayerProfiles DO
		if (url.pathname === '/api/profile' || url.pathname === '/api/profiles') {
			const id = env.PLAYER_PROFILES.idFromName('global');
			return env.PLAYER_PROFILES.get(id).fetch(request);
		}

		// REST: generate a fresh room code (client then connects via /ws)
		if (url.pathname === '/api/create' && request.method === 'POST') {
			const code = generateCode();
			return new Response(JSON.stringify({ code }), {
				headers: { ...cors, 'Content-Type': 'application/json' },
			});
		}

		// WebSocket: /ws?code=XXXXXX&name=PlayerName
		if (url.pathname === '/ws') {
			const code = url.searchParams.get('code');
			if (!code) return new Response('Missing ?code param', { status: 400 });
			const id = env.GAME_ROOMS.idFromName(code.toUpperCase());
			return env.GAME_ROOMS.get(id).fetch(request);
		}

		return new Response(
			'Fallout 67 v1.1\nPOST /api/create      →  get a room code\nGET  /ws?code=X&name=Y →  WebSocket\nPOST /api/score       →  submit score\nGET  /api/leaderboard  →  top 20 scores\nPOST /api/profile     →  upsert profile\nGET  /api/profile?name=X → fetch profile',
			{ headers: { 'Content-Type': 'text/plain', ...cors } },
		);
	},
} satisfies ExportedHandler<Env>;
