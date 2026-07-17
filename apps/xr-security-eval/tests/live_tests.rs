//! Correctness of the live code-admission streaming logic (checks 1–6 of the
//! agreed test list). These are pure-logic tests over the same real validator
//! the batch evaluator uses — no HTTP, no browser.

use std::collections::HashSet;

use xr_security_eval::live::{
    admit, build_case_event, build_custom_event, collect_run, is_known_runtime_residual,
};
use xr_security_eval::{evaluate, evaluate_attack, load_corpus, Defence};

const BANNED_PREFIX: &str = "banned API/token referenced: ";

// (1) Every streamed verdict exactly matches the batch evaluator.
#[test]
fn admit_matches_batch_evaluate_for_every_payload_and_level() {
    let corpus = load_corpus();
    for a in &corpus.attacks {
        for d in Defence::ALL {
            let batch = evaluate(&a.csharp, d);
            let live = admit(&a.csharp, d);
            assert_eq!(
                batch.blocked, live.blocked,
                "block mismatch {} {:?}",
                a.id, d
            );
            assert_eq!(
                batch.reasons.len(),
                live.violations.len(),
                "violation count mismatch {} {:?}",
                a.id,
                d
            );
        }
    }
}

// (1b) The per-case event's blocked flag matches the batch AttackReport triple.
#[test]
fn case_event_blocked_matches_attack_report() {
    let corpus = load_corpus();
    for a in &corpus.attacks {
        let rep = evaluate_attack(a);
        let none = build_case_event("run-1", 0, 1, a, Defence::None);
        let sec = build_case_event("run-1", 0, 1, a, Defence::SecurityOnly);
        let hard = build_case_event("run-1", 0, 1, a, Defence::FullyHardened);
        assert_eq!(none.verdict.blocked, rep.blocked[0], "{} none", a.id);
        assert_eq!(sec.verdict.blocked, rep.blocked[1], "{} sec", a.id);
        assert_eq!(hard.verdict.blocked, rep.blocked[2], "{} hard", a.id);
    }
}

// (2) All corpus records arrive exactly once, in order, then a completion event.
#[test]
fn collect_run_streams_all_cases_once_in_order_then_summary() {
    let corpus = load_corpus();
    let ids: Vec<String> = corpus.attacks.iter().map(|a| a.id.clone()).collect();
    let events = collect_run(&corpus.attacks, "run-1", &ids, Defence::FullyHardened);

    assert_eq!(events.len(), ids.len() + 1, "N cases + 1 summary");
    assert_eq!(events.last().unwrap()["type"], "completed");

    let mut seen = HashSet::new();
    for (i, id) in ids.iter().enumerate() {
        let ev = &events[i];
        assert_eq!(ev["index"].as_u64().unwrap() as usize, i, "index {i}");
        assert_eq!(ev["payload_id"].as_str().unwrap(), id, "order at {i}");
        assert_eq!(ev["run_id"].as_str().unwrap(), "run-1");
        assert_eq!(ev["total"].as_u64().unwrap() as usize, ids.len());
        assert!(seen.insert(id.clone()), "duplicate {id}");
    }
    assert_eq!(seen.len(), ids.len(), "every id exactly once");
}

// (3) The completion summary recomputes correctly from the streamed cases.
#[test]
fn summary_recomputes_and_matches_paper_95pct() {
    let corpus = load_corpus();
    let ids: Vec<String> = corpus.attacks.iter().map(|a| a.id.clone()).collect();
    let events = collect_run(&corpus.attacks, "run-9", &ids, Defence::FullyHardened);
    let (summary, cases) = events.split_last().unwrap();

    let rejected = cases.iter().filter(|e| e["decision"] == "reject").count();
    assert_eq!(summary["total"].as_u64().unwrap() as usize, ids.len());
    assert_eq!(summary["rejected"].as_u64().unwrap() as usize, rejected);
    assert_eq!(
        summary["admitted"].as_u64().unwrap() as usize,
        ids.len() - rejected
    );
    // 40 malicious, 2 documented residuals admitted => 38 rejected (the paper's 95%).
    assert_eq!(rejected, 38, "38/40 malicious rejected at DeployHardened");
    // median/p95 present and non-negative.
    assert!(summary["median_latency_us"].as_f64().unwrap() >= 0.0);
    assert!(summary["p95_latency_us"].as_f64().unwrap() >= 0.0);
}

// (4) Security-only vs DeployHardened violations are classified into the right layer.
#[test]
fn violation_layers_split_security_vs_perceptual_correctly() {
    let corpus = load_corpus();
    let mut saw_system = false;
    let mut saw_perceptual = false;

    for a in &corpus.attacks {
        // CreativeFreedom reason-set is the reference that a perceptual token is absent from.
        let sec_set: HashSet<String> = evaluate(&a.csharp, Defence::SecurityOnly)
            .reasons
            .into_iter()
            .collect();
        let hard = evaluate(&a.csharp, Defence::FullyHardened); // full messages, in order
        let live = admit(&a.csharp, Defence::FullyHardened); // index-aligned layers

        assert_eq!(hard.reasons.len(), live.violation_layers.len());
        for (i, msg) in hard.reasons.iter().enumerate() {
            let expected = if !msg.starts_with(BANNED_PREFIX) {
                "structural"
            } else if sec_set.contains(msg) {
                "system_security"
            } else {
                "perceptual_xr"
            };
            assert_eq!(
                live.violation_layers[i], expected,
                "layer for `{msg}` on {}",
                a.id
            );
            match expected {
                "system_security" => saw_system = true,
                "perceptual_xr" => saw_perceptual = true,
                _ => {}
            }
        }
    }
    assert!(saw_system, "expected at least one system_security ban");
    assert!(saw_perceptual, "expected at least one perceptual_xr ban");
}

// (5) joy-05 / joy-06 are surfaced as documented runtime residuals, and no OTHER
// malicious payload is admitted at DeployHardened.
#[test]
fn joystick_residuals_flagged_and_no_other_malicious_admitted() {
    let corpus = load_corpus();
    for id in ["joy-05", "joy-06"] {
        let a = corpus.attacks.iter().find(|a| a.id == id).unwrap();
        assert!(a.should_block, "{id} is malicious");
        let ev = build_case_event("run-1", 0, 1, a, Defence::FullyHardened);
        assert!(!ev.verdict.blocked, "{id} admitted (documented residual)");
        assert_eq!(ev.expected_classification, "known_runtime_residual");
        assert_eq!(ev.verdict_label, "ADMITTED — KNOWN RUNTIME-GUARDIAN CASE");
        assert!(ev.runtime_unverified);
        assert!(is_known_runtime_residual(id));
    }
    for a in &corpus.attacks {
        if a.should_block && !is_known_runtime_residual(&a.id) {
            let ev = build_case_event("run-1", 0, 1, a, Defence::FullyHardened);
            assert!(ev.verdict.blocked, "unexpected admit of malicious {}", a.id);
        }
    }
}

// (6) Arbitrary admitted custom C# is never labelled safe/benign.
#[test]
fn admitted_custom_csharp_is_never_labelled_safe() {
    let cs = "public class GeneratedBehaviour : MonoBehaviour { void Start() { \
              transform.position = new Vector3(0f, 1f, 0f); } }";
    let ev = build_custom_event(cs, Defence::FullyHardened);
    assert!(
        !ev.verdict.blocked,
        "this benign-looking snippet is admitted"
    );
    assert_eq!(ev.expected_classification, "unknown_custom");
    assert_eq!(ev.verdict_label, "ADMITTED BY STATIC VALIDATOR");
    assert!(ev.runtime_unverified, "runtime caveat must be set");
    let l = ev.verdict_label.to_lowercase();
    assert!(
        !l.contains("safe") && !l.contains("benign"),
        "never claims safety"
    );
}

// (6b) A malicious custom paste is still rejected (the enforcing lane works).
#[test]
fn malicious_custom_csharp_is_rejected() {
    let cs = "public class GeneratedBehaviour : MonoBehaviour { void Start() { \
              var w = new System.Net.WebClient(); } }";
    let ev = build_custom_event(cs, Defence::FullyHardened);
    assert!(ev.verdict.blocked, "network exfiltration must be rejected");
    assert_eq!(ev.verdict_label, "REJECTED BEFORE EXECUTION");
    assert!(!ev.runtime_unverified);
}
