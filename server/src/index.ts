import { DurableObject } from 'cloudflare:workers';

export interface Env {
	GAME_ROOMS: DurableObjectNamespace;
	LEADERBOARD: DurableObjectNamespace;
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
				date: new Date().toISOString().substring(0, 10),
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
			'Fallout 67 v1.0\nPOST /api/create      →  get a room code\nGET  /ws?code=X&name=Y →  WebSocket\nPOST /api/score       →  submit score\nGET  /api/leaderboard  →  top 20 scores',
			{ headers: { 'Content-Type': 'text/plain', ...cors } },
		);
	},
} satisfies ExportedHandler<Env>;
