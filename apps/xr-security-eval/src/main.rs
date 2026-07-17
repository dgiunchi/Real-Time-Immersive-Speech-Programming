//! CLI: run the whole corpus through the real validator at 3 defence levels, print the
//! headline table, and write results.json for the figure/report generator.

use std::collections::BTreeMap;

use serde::Serialize;
use xr_security_eval::{evaluate_attack, load_corpus, AttackReport, Defence};

#[derive(Serialize)]
struct ClassRow {
    class: String,
    n: usize,
    /// attacks that still SUCCEED (were not blocked) at [none, security, hardened].
    succeed: [usize; 3],
}

#[derive(Serialize)]
struct BenignRow {
    n: usize,
    /// benign commands wrongly BLOCKED (over-blocking) at [none, security, hardened].
    overblocked: [usize; 3],
}

#[derive(Serialize)]
struct Summary {
    attack_classes: Vec<ClassRow>,
    overall: ClassRow,
    benign: BenignRow,
    mitigation_rate_hardened: f64,
}

fn pct(a: usize, b: usize) -> f64 {
    if b == 0 {
        0.0
    } else {
        100.0 * a as f64 / b as f64
    }
}

fn main() {
    let corpus = load_corpus();
    let reports: Vec<AttackReport> = corpus.attacks.iter().map(evaluate_attack).collect();

    // ---- aggregate ----
    let mut by_class: BTreeMap<String, [usize; 4]> = BTreeMap::new(); // [n, succ_none, succ_sec, succ_hard]
    let mut overall = [0usize; 4];
    let mut benign = [0usize; 4]; // [n, over_none, over_sec, over_hard]
    for r in &reports {
        if r.should_block {
            let e = by_class.entry(r.cls.clone()).or_insert([0; 4]);
            e[0] += 1;
            overall[0] += 1;
            for lvl in 0..3 {
                if !r.blocked[lvl] {
                    e[lvl + 1] += 1;
                    overall[lvl + 1] += 1;
                }
            }
        } else {
            benign[0] += 1;
            for lvl in 0..3 {
                if r.blocked[lvl] {
                    benign[lvl + 1] += 1;
                }
            }
        }
    }

    // ---- headline table ----
    println!("\n=========================================================================");
    println!(" XR privacy & manipulation attacks vs the DreamCodeVR+ backend");
    println!(
        " Attack SUCCESS rate (lower = better defence) — {} attacks, 5 classes",
        overall[0]
    );
    println!("=========================================================================");
    println!(
        "{:<14} {:>3}   {:>14} {:>14} {:>14}",
        "class", "N", "no-defence", "security-only", "fully-hardened"
    );
    println!("{}", "-".repeat(73));
    let cell = |succ: usize, n: usize| format!("{}/{} ({:>3.0}%)", succ, n, pct(succ, n));
    let mut class_rows = Vec::new();
    for (cls, e) in &by_class {
        println!(
            "{:<14} {:>3}   {:>14} {:>14} {:>14}",
            cls,
            e[0],
            cell(e[1], e[0]),
            cell(e[2], e[0]),
            cell(e[3], e[0])
        );
        class_rows.push(ClassRow {
            class: cls.clone(),
            n: e[0],
            succeed: [e[1], e[2], e[3]],
        });
    }
    println!("{}", "-".repeat(73));
    println!(
        "{:<14} {:>3}   {:>14} {:>14} {:>14}",
        "OVERALL",
        overall[0],
        cell(overall[1], overall[0]),
        cell(overall[2], overall[0]),
        cell(overall[3], overall[0])
    );

    let blocked_hard = overall[0] - overall[3];
    let mitigation = pct(blocked_hard, overall[0]);
    println!(
        "\n Mitigation (fully-hardened): {}/{} attacks blocked = {:.0}%",
        blocked_hard, overall[0], mitigation
    );
    println!(
        " Residual: {}/{} slip past static validation (bare camera-transform herding —",
        overall[3], overall[0]
    );
    println!("           requires the runtime UserFrameGuardian / Mode-D sandbox; see writeup).");

    println!(
        "\n Benign creative commands (creative freedom preserved) — {} commands",
        benign[0]
    );
    println!(
        "   over-blocked: {}/{} at security-only, {}/{} at fully-hardened  (pass rate {:.0}%)",
        benign[2],
        benign[0],
        benign[3],
        benign[0],
        pct(benign[0] - benign[3], benign[0])
    );
    println!("=========================================================================\n");

    // ---- write results.json ----
    let summary = Summary {
        attack_classes: class_rows,
        overall: ClassRow {
            class: "overall".into(),
            n: overall[0],
            succeed: [overall[1], overall[2], overall[3]],
        },
        benign: BenignRow {
            n: benign[0],
            overblocked: [benign[1], benign[2], benign[3]],
        },
        mitigation_rate_hardened: mitigation,
    };
    let out = serde_json::json!({
        "levels": [Defence::None.label(), Defence::SecurityOnly.label(), Defence::FullyHardened.label()],
        "summary": summary,
        "per_attack": reports,
    });
    let path = concat!(env!("CARGO_MANIFEST_DIR"), "/results.json");
    std::fs::write(path, serde_json::to_string_pretty(&out).unwrap()).expect("write results.json");
    println!("wrote {path}");
}
