//! Age-adaptive safety posture — the novel coupling.
//!
//! One age signal (from the on-device ML age gate, or the platform age flag)
//! conditions BOTH safety planes at once:
//!   * the **code-safety** plane — whether generated C# is validated under the
//!     stricter `DeployHardened` perceptual/device denylist, and
//!   * the **perceptual/embodied** plane — the comfort/exposure bounds (flash rate,
//!     field-of-view coverage, rotation speed, luminance swing, spawn budget) and
//!     whether a runtime compile needs explicit confirmation.
//!
//! FAIL-SAFE: an `Unknown`/low-confidence band maps to the STRICTEST (child) posture,
//! never to the permissive one — age RESTRICTS, it never AUTHORISES. The platform age
//! flag remains authoritative; this posture is a safety net, never the legal basis for
//! consent.

/// A coarse, privacy-preserving age band. This is the ONLY age value that ever leaves
/// the on-device model — never a precise age, never a voiceprint/template.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum AgeBand {
    /// Under 13 (COPPA line).
    Child,
    /// 13–17.
    Teen,
    /// 18+.
    Adult,
    /// Not yet determined / below the confidence threshold. Treated as `Child`.
    #[default]
    Unknown,
}

impl AgeBand {
    pub fn from_token(s: &str) -> AgeBand {
        match s.trim().to_ascii_lowercase().as_str() {
            "child" | "preteen" | "under13" => AgeBand::Child,
            "teen" | "teenager" => AgeBand::Teen,
            "adult" => AgeBand::Adult,
            _ => AgeBand::Unknown,
        }
    }

    pub fn as_str(&self) -> &'static str {
        match self {
            AgeBand::Child => "child",
            AgeBand::Teen => "teen",
            AgeBand::Adult => "adult",
            AgeBand::Unknown => "unknown",
        }
    }

    /// A minor (or unknown, which fails safe to minor). Minors get the hardened
    /// code-safety denylist automatically.
    pub fn is_minor(&self) -> bool {
        matches!(self, AgeBand::Child | AgeBand::Teen | AgeBand::Unknown)
    }
}

/// The concrete safety tightenings a given age band implies, spanning BOTH planes.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AgeSafetyPosture {
    // --- code-safety plane ---
    /// Validate generated C# under `DeployHardened` (perceptual/device denylist on).
    pub perceptual_hardening: bool,
    /// Require an explicit user confirmation before a Mode-A runtime compile.
    pub require_compile_confirmation: bool,
    /// Max objects one command may spawn.
    pub max_spawn_per_command: u32,
    // --- perceptual/embodied plane (bounds the generated scene) ---
    /// Max flashing rate (Hz). WCAG 2.3 sets 3 Hz as the general floor; minors get
    /// tighter still (children are more photosensitive-vulnerable).
    pub max_flash_hz: f32,
    /// Max fraction of the field of view a generated overlay may cover.
    pub max_fov_coverage: f32,
    /// Max comfort rotation speed (deg/s) applied to the view/world.
    pub max_rotate_deg_s: f32,
    /// Max luminance swing (0..1) a single command may cause (anti dark/blast).
    pub max_luminance_delta: f32,
}

impl AgeSafetyPosture {
    /// Map an age band to its safety posture. Child = strictest; Adult = permissive;
    /// Teen = in between; Unknown = Child (fail-safe).
    pub fn for_band(band: AgeBand) -> AgeSafetyPosture {
        match band {
            AgeBand::Child | AgeBand::Unknown => AgeSafetyPosture {
                perceptual_hardening: true,
                require_compile_confirmation: true,
                max_spawn_per_command: 20,
                max_flash_hz: 2.0,
                max_fov_coverage: 0.35,
                max_rotate_deg_s: 30.0,
                max_luminance_delta: 0.4,
            },
            AgeBand::Teen => AgeSafetyPosture {
                perceptual_hardening: true,
                require_compile_confirmation: true,
                max_spawn_per_command: 30,
                max_flash_hz: 3.0,
                max_fov_coverage: 0.5,
                max_rotate_deg_s: 45.0,
                max_luminance_delta: 0.6,
            },
            AgeBand::Adult => AgeSafetyPosture {
                perceptual_hardening: false,
                require_compile_confirmation: false,
                max_spawn_per_command: 40,
                max_flash_hz: 3.0, // WCAG floor still applies to everyone
                max_fov_coverage: 0.7,
                max_rotate_deg_s: 90.0,
                max_luminance_delta: 1.0,
            },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn unknown_fails_safe_to_child() {
        // The load-bearing safety invariant: an undetermined band is NEVER permissive.
        assert_eq!(
            AgeSafetyPosture::for_band(AgeBand::Unknown),
            AgeSafetyPosture::for_band(AgeBand::Child)
        );
        assert!(AgeBand::Unknown.is_minor());
    }

    #[test]
    fn child_tightens_both_planes_vs_adult() {
        // THE novel coupling: one age band moves both the code-safety plane and the
        // perceptual plane at once.
        let child = AgeSafetyPosture::for_band(AgeBand::Child);
        let adult = AgeSafetyPosture::for_band(AgeBand::Adult);
        // code-safety plane:
        assert!(child.perceptual_hardening && !adult.perceptual_hardening);
        assert!(child.require_compile_confirmation && !adult.require_compile_confirmation);
        assert!(child.max_spawn_per_command < adult.max_spawn_per_command);
        // perceptual plane:
        assert!(child.max_flash_hz < adult.max_flash_hz);
        assert!(child.max_fov_coverage < adult.max_fov_coverage);
        assert!(child.max_rotate_deg_s < adult.max_rotate_deg_s);
        assert!(child.max_luminance_delta < adult.max_luminance_delta);
    }

    #[test]
    fn posture_is_monotonic_by_strictness() {
        // Child <= Teen <= Adult on every permissive bound (never a reversal).
        let c = AgeSafetyPosture::for_band(AgeBand::Child);
        let t = AgeSafetyPosture::for_band(AgeBand::Teen);
        let a = AgeSafetyPosture::for_band(AgeBand::Adult);
        assert!(
            c.max_fov_coverage <= t.max_fov_coverage && t.max_fov_coverage <= a.max_fov_coverage
        );
        assert!(
            c.max_rotate_deg_s <= t.max_rotate_deg_s && t.max_rotate_deg_s <= a.max_rotate_deg_s
        );
        assert!(
            c.max_spawn_per_command <= t.max_spawn_per_command
                && t.max_spawn_per_command <= a.max_spawn_per_command
        );
    }

    #[test]
    fn band_token_roundtrip_and_fallback() {
        assert_eq!(AgeBand::from_token("Child"), AgeBand::Child);
        assert_eq!(AgeBand::from_token(" TEEN "), AgeBand::Teen);
        assert_eq!(AgeBand::from_token("adult"), AgeBand::Adult);
        assert_eq!(AgeBand::from_token("garbage"), AgeBand::Unknown); // unknown => fail-safe
        assert!(!AgeBand::Adult.is_minor());
    }
}
