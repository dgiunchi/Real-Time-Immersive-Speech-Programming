//! Which model DreamCodeVR+ generates with, in one place.
//!
//! Chosen by measurement, not by reputation. `apps/model-bench` ran every candidate through
//! the REAL pipeline — same system prompt, same guardrail, same compiler, same repair policy
//! — on 30 creative prompts, 20 of which appear nowhere in the prompt or its examples.
//! Results in `artifacts/model-benchmark-stage-{a,b}.json`.
//!
//! # What the data actually said
//!
//! ```text
//! CONFIG               N  1st-pass compile  named parts  p50      p95
//! gpt-5.6-luna:low    30              100%          91%  7.8 s   12.6 s
//! gpt-5.6-terra:low   30              100%          63%  11.6 s  17.5 s
//! gpt-5.6-luna:xhigh  30              100%          83%  43.8 s  76.4 s
//! gpt-5.4-nano:low    10               60%          98%  12.9 s  23.4 s   <- previous default
//! gpt-5.6-sol:medium  10              100%          50%  32.2 s  52.3 s
//! ```
//!
//! Three findings worth stating plainly, because two of them contradict the obvious guess:
//!
//! 1. **The old default was the bottleneck.** `gpt-5.4-nano` compiled on the first attempt
//!    only 60% of the time. That single number explains the "creative generation fails
//!    about half the time" behaviour seen on device — it was the model, not the pipeline.
//! 2. **More reasoning effort made this task WORSE.** `xhigh` cost 5.6x the latency and
//!    named fewer parts (83% vs 91%). Longer deliberation produced more elaborate code with
//!    more anonymous helper geometry, and in a VR authoring tool an unnamed part is a part
//!    the user cannot talk about.
//! 3. **The largest model was not the best one.** `sol` was the slowest tested and named
//!    half its parts. Nothing about our task rewards its extra capability.
//!
//! # Why the ladder is honest rather than flattering
//!
//! Every 5.6 configuration compiled 100% of the time, so the presets below cannot be sold
//! as a quality ladder — the measurable differences are latency and naming, and both favour
//! the cheapest option. The four presets exist because a user may want the choice, and MAX
//! is genuinely the most deliberate configuration available; but the recommendation is
//! [`Preset::Low`], and pretending otherwise would be choosing marketing over data.

/// A user-facing quality/latency choice.
///
/// Deliberately NOT the same vocabulary as the API's `reasoning_effort`: the API rejects
/// `max` entirely (measured — it names `xhigh` as the ceiling), and a preset may change
/// model as well as effort. Keeping our names separate from theirs means a provider change
/// does not become a user-visible one.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Preset {
    Low,
    Medium,
    High,
    Max,
}

/// A resolved model configuration.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ModelChoice {
    pub model: &'static str,
    /// An effort the API actually accepts. `none | low | medium | high | xhigh`.
    pub effort: &'static str,
}

impl Preset {
    pub fn parse(s: &str) -> Option<Preset> {
        match s.trim().to_ascii_lowercase().as_str() {
            "low" | "fast" => Some(Preset::Low),
            "medium" | "balanced" => Some(Preset::Medium),
            "high" => Some(Preset::High),
            "max" | "best" => Some(Preset::Max),
            _ => None,
        }
    }

    /// The measured configuration for this preset.
    pub fn resolve(self) -> ModelChoice {
        match self {
            // Fastest, and also the best measured: 100% first-pass compile, 91% named
            // parts, 7.8 s median.
            Preset::Low => ModelChoice {
                model: LUNA,
                effort: "low",
            },
            // A little more deliberation. Screening showed no compile benefit and slightly
            // worse naming, so this is offered rather than recommended.
            Preset::Medium => ModelChoice {
                model: LUNA,
                effort: "medium",
            },
            // A different model rather than more effort — the only axis left once effort
            // stopped helping. Slower, and it named 63% of parts.
            Preset::High => ModelChoice {
                model: TERRA,
                effort: "low",
            },
            // The most deliberate configuration the API supports. `max` is NOT an accepted
            // API value; `xhigh` is the real ceiling.
            Preset::Max => ModelChoice {
                model: LUNA,
                effort: "xhigh",
            },
        }
    }
}

pub const LUNA: &str = "gpt-5.6-luna";
pub const TERRA: &str = "gpt-5.6-terra";

/// The production default.
///
/// Not MAX. VR authoring is interactive, and the benchmark found no quality left to buy:
/// every 5.6 configuration compiled 100% of the time, so a slower preset trades seconds of
/// silence in a headset for nothing measurable. `Low` was also the BEST on naming, which is
/// what makes "remove the north west tower" work at all.
pub const DEFAULT_PRESET: Preset = Preset::Low;

/// Repairing non-compiling C# uses the same model as generating it.
///
/// Deliberately not a second, stronger model: across 90 finalist calls the repair loop was
/// never needed (0.00 repairs per request), so there is no measurement that would justify
/// the extra configuration surface. Revisit if a future model regresses on first-pass
/// compilation.
pub fn repair_choice(generation: &ModelChoice) -> ModelChoice {
    generation.clone()
}

#[cfg(test)]
#[allow(clippy::unwrap_used)]
mod tests {
    use super::*;

    #[test]
    fn every_preset_resolves_to_an_effort_the_api_accepts() {
        // Measured against the live API: `max` is rejected for all candidate models and
        // `xhigh` is the ceiling. A preset that resolves to an unsupported effort would
        // fail every request at runtime.
        const ACCEPTED: [&str; 5] = ["none", "low", "medium", "high", "xhigh"];
        for p in [Preset::Low, Preset::Medium, Preset::High, Preset::Max] {
            let c = p.resolve();
            assert!(ACCEPTED.contains(&c.effort), "{p:?} -> {c:?}");
            assert!(!c.model.is_empty());
        }
    }

    #[test]
    fn the_default_is_the_measured_winner_not_the_slowest_option() {
        let d = DEFAULT_PRESET.resolve();
        assert_eq!(d.model, LUNA);
        assert_eq!(d.effort, "low");
        assert_ne!(
            DEFAULT_PRESET,
            Preset::Max,
            "the default must be the quality/latency knee, not the most expensive preset"
        );
    }

    #[test]
    fn preset_names_parse_and_round_trip() {
        assert_eq!(Preset::parse("LOW"), Some(Preset::Low));
        assert_eq!(Preset::parse(" max "), Some(Preset::Max));
        assert_eq!(Preset::parse("nonsense"), None);
    }
}
