using System;

namespace fallover_67
{
    /// <summary>
    /// Screen-shake effect triggered by explosions.
    /// Maintains a decaying intensity value and produces per-frame random offsets.
    /// Thread-safe: Update() runs on the render thread, GetOffset() is called during Paint.
    /// </summary>
    public class CameraShake
    {
        private float _intensity;           // current shake magnitude in pixels
        private float _trauma;              // 0–1 "damage" value (squared = intensity)
        private readonly Random _rng = new Random();

        // ── Tuning knobs ──────────────────────────────────────────────────────
        private const float MaxTrauma = 1.0f;
        private const float TraumaDecay = 1.8f;    // trauma units lost per second
        private const float MaxOffsetPx = 8.0f;    // max pixel displacement at full trauma

        // Blast-radius → trauma conversion
        private const float TraumaPerRadius = 0.012f;   // 45-radius blast ≈ 0.54 trauma
        private const float MinTraumaAdd = 0.15f;

        // When many explosions overlap, cap how much new trauma can stack
        private const float StackCooldown = 0.12f;      // seconds between full-strength adds
        private float _timeSinceLastAdd;

        /// <summary>Current X offset to apply to the Graphics transform.</summary>
        public float OffsetX { get; private set; }

        /// <summary>Current Y offset to apply to the Graphics transform.</summary>
        public float OffsetY { get; private set; }

        /// <summary>Whether the shake is currently active (non-zero).</summary>
        public bool IsActive => _trauma > 0.001f;

        /// <summary>
        /// Call once per render-thread tick to decay trauma and compute new offsets.
        /// </summary>
        public void Update(float dt)
        {
            _timeSinceLastAdd += dt;

            if (_trauma > 0)
            {
                _trauma = Math.Max(0f, _trauma - TraumaDecay * dt);

                // Intensity = trauma² gives a nice non-linear falloff
                _intensity = _trauma * _trauma * MaxOffsetPx;

                // Produce random offset in [-intensity, +intensity]
                OffsetX = ((float)_rng.NextDouble() * 2f - 1f) * _intensity;
                OffsetY = ((float)_rng.NextDouble() * 2f - 1f) * _intensity;
            }
            else
            {
                OffsetX = 0;
                OffsetY = 0;
            }
        }

        /// <summary>
        /// Add trauma from an explosion. Larger blasts shake harder.
        /// Rapid successive calls are dampened to prevent overwhelming shaking.
        /// </summary>
        /// <param name="blastRadius">The MaxRadius of the explosion (typically 40–70).</param>
        public void AddTrauma(float blastRadius)
        {
            float amount = Math.Max(MinTraumaAdd, blastRadius * TraumaPerRadius);

            // Dampen rapid stacking: if another explosion just added trauma,
            // reduce the contribution so 5 simultaneous hits ≠ 5× a single hit
            if (_timeSinceLastAdd < StackCooldown)
            {
                float dampFactor = _timeSinceLastAdd / StackCooldown; // 0→1
                amount *= Math.Max(0.25f, dampFactor);                // at least 25%
            }

            _trauma = Math.Min(MaxTrauma, _trauma + amount);
            _timeSinceLastAdd = 0f;
        }

        /// <summary>
        /// Immediately stop all shaking (e.g., on form close or state reset).
        /// </summary>
        public void Reset()
        {
            _trauma = 0;
            _intensity = 0;
            OffsetX = 0;
            OffsetY = 0;
        }
    }
}
