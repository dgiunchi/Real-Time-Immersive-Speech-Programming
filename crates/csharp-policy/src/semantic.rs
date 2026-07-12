//! Semantic / intent dark-pattern screen (TRACK U3) — a FIRST-PASS heuristic.
//!
//! The capability-removal layers (allow-list, lexical bans, deploy-hardened
//! profile) stop attacks that need a specific *API*. They cannot see *intent*:
//! a dark pattern (Krauss et al. 2024) or a swapping/disguise attack (Tseng et al.
//! 2022; Fujita et al. 2023) uses entirely legitimate operations with a deceptive
//! purpose. This module is the scaffold for the eventual semantic classifier.
//!
//! It is deliberately ADVISORY-ONLY and OFF by default: it NEVER blocks a command
//! or a build (that would censor creation and produce false positives). It returns
//! human-readable advisories that the disclosure channel / admin panel can surface,
//! so an intent-class concern is made VISIBLE without restricting freedom. The
//! production-grade classifier (an ML model) is named as future work in the thesis.
//!
//! Pure: `std` only, no allocation beyond the returned advisories, never panics.

/// One intent-class advisory. Carries the dark-pattern cluster (per Krauss et al.'s
/// taxonomy / Tseng's mismatching families), a human-readable note, and a coarse
/// heuristic confidence in `[0, 1]`.
#[derive(Debug, Clone, PartialEq)]
pub struct SemanticAdvisory {
    pub cluster: &'static str,
    pub note: String,
    pub confidence: f32,
}

/// A signature: cluster, the confidence to report, and the term groups that must
/// ALL be present (each group is an OR over synonyms). Empty groups never match.
struct Signature {
    cluster: &'static str,
    note: &'static str,
    confidence: f32,
    all_of: &'static [&'static [&'static str]],
}

const SIGNATURES: &[Signature] = &[
    // Tseng swapping / Krauss "disguising actions" — render an identity/threat onto
    // a real person to provoke a real-world action against them.
    Signature {
        cluster: "swapping",
        note: "content may render a threat/enemy identity onto a real person/bystander \
               (Tseng 2022 swapping; provokes action against a real human)",
        confidence: 0.6,
        all_of: &[
            &["bystander", "person", "someone", "people", "face", "human"],
            &[
                "zombie", "enemy", "monster", "target", "attack", "punch", "hit", "shoot",
            ],
        ],
    },
    // Krauss "exploiting the drive to succeed" / coercion via a loaded gesture
    // (the Puppy-Crushing pattern): forcing a meaningful physical act to proceed.
    Signature {
        cluster: "coercion",
        note: "content may coerce a loaded physical gesture to proceed/decline \
               (Krauss 2024 gesture coercion / Puppy-Crushing)",
        confidence: 0.55,
        all_of: &[
            &["step on", "stomp", "kick", "crush", "stamp", "hit"],
            &[
                "to continue",
                "to decline",
                "to close",
                "to proceed",
                "to dismiss",
                "to skip",
            ],
        ],
    },
    // Tseng false-positive / Fujita disguise — a fake passed off as a real,
    // trusted, physical object the user will act on (sit / lean / step).
    Signature {
        cluster: "false-affordance",
        note: "content may present a virtual object as a real, trusted physical one \
               (Tseng 2022 false-positive; Fujita 2023 disguise)",
        confidence: 0.45,
        all_of: &[
            &[
                "real",
                "indistinguishable",
                "authentic",
                "looks like",
                "disguise",
                "fake",
            ],
            &[
                "chair", "floor", "step", "stair", "ledge", "seat", "sit", "lean",
            ],
        ],
    },
    // Tseng false-negative — hide a real hazard or a needed instruction/warning.
    Signature {
        cluster: "concealment",
        note: "content may hide a real hazard / required warning or instruction \
               (Tseng 2022 false-negative)",
        confidence: 0.5,
        all_of: &[
            &["hide", "remove", "mute", "suppress", "block", "cover"],
            &[
                "warning",
                "hazard",
                "exit",
                "window",
                "danger",
                "instruction",
                "sign",
                "alert",
            ],
        ],
    },
    // Krauss perception cluster — motion/sickness used as punishment for a choice.
    Signature {
        cluster: "motion-punishment",
        note: "content may induce discomfort/sickness as punishment for a choice \
               (Krauss 2024 motion-as-punishment)",
        confidence: 0.5,
        all_of: &[
            &[
                "nauseat",
                "dizzy",
                "sick",
                "vomit",
                "strobe",
                "flash",
                "spin fast",
                "shake",
            ],
            &[
                "punish",
                "if they",
                "discourage",
                "for choosing",
                "until they",
                "penalty",
            ],
        ],
    },
];

fn group_matches(haystack: &str, group: &[&str]) -> bool {
    group.iter().any(|term| haystack.contains(term))
}

/// Screen free text (a spoken command and/or generated C#) for intent-class
/// dark-pattern / mismatching signatures. ADVISORY-ONLY: the result NEVER blocks;
/// callers surface it for transparency. Case-insensitive; returns one advisory per
/// matched signature (deduplicated by cluster, highest confidence kept).
pub fn semantic_screen(text: &str) -> Vec<SemanticAdvisory> {
    let lc = text.to_ascii_lowercase();
    let mut out: Vec<SemanticAdvisory> = Vec::new();
    for sig in SIGNATURES {
        if !sig.all_of.is_empty() && sig.all_of.iter().all(|g| group_matches(&lc, g)) {
            // Keep the highest-confidence advisory per cluster.
            if let Some(existing) = out.iter_mut().find(|a| a.cluster == sig.cluster) {
                if sig.confidence > existing.confidence {
                    existing.confidence = sig.confidence;
                    existing.note = sig.note.to_string();
                }
            } else {
                out.push(SemanticAdvisory {
                    cluster: sig.cluster,
                    note: sig.note.to_string(),
                    confidence: sig.confidence,
                });
            }
        }
    }
    out
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    #[test]
    fn benign_creative_command_has_no_advisory() {
        // FREEDOM CONTRACT: ordinary creative commands must produce NO advisory.
        assert!(semantic_screen("make a small spooky house with a red roof").is_empty());
        assert!(semantic_screen("build a solar system with spinning planets").is_empty());
        assert!(semantic_screen("make it ocean blue and gently bob up and down").is_empty());
        assert!(semantic_screen("create a snowman next to the cube").is_empty());
    }

    #[test]
    fn swapping_onto_a_person_is_flagged() {
        let a = semantic_screen("render a zombie over that bystander so I attack them");
        assert!(a.iter().any(|x| x.cluster == "swapping"));
    }

    #[test]
    fn gesture_coercion_is_flagged() {
        let a = semantic_screen("the user must step on the puppy to decline the offer");
        assert!(a.iter().any(|x| x.cluster == "coercion"));
    }

    #[test]
    fn concealment_of_a_hazard_is_flagged() {
        let a = semantic_screen("hide the warning sign near the open window");
        assert!(a.iter().any(|x| x.cluster == "concealment"));
    }

    #[test]
    fn advisory_is_never_a_block() {
        // The function only ever RETURNS advisories; it has no reject path. A flagged
        // command still proceeds — the caller discloses, it does not censor.
        let a = semantic_screen("render an enemy monster onto that person to punch");
        assert!(!a.is_empty());
        // Confidence is bounded and coarse (heuristic, not a verdict).
        assert!(a.iter().all(|x| x.confidence > 0.0 && x.confidence <= 1.0));
    }
}
