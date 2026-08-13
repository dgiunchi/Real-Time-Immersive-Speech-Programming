//! Which model should DreamCodeVR+ generate with?
//!
//! Not "which model is best at coding" — which model is best at THIS task: producing Unity
//! C# that passes our guardrail unchanged, compiles, exposes one unambiguous entry point,
//! and names its parts in words a person would say out loud. A leaderboard cannot answer
//! that, so this measures it.
//!
//! # Why it reuses the real pipeline instead of reimplementing it
//!
//! Every configuration goes through the SAME `OpenAiLlmClient` (so the same authoritative
//! system prompt and the same JSON schema), the SAME `dcvr-csharp-policy` guardrail, and
//! the SAME `/compile` service the headset path uses. Only the model id and the reasoning
//! effort change between runs. A harness with its own prompt would be benchmarking itself.
//!
//! # What it deliberately separates
//!
//! First-pass quality is recorded apart from post-repair quality. A model that needs two
//! repairs to compile is not equivalent to one that compiles immediately, even if both end
//! up working — the difference is seconds of silence in a headset. Truncation is recorded
//! apart from bad code, because a candidate cut off by OUR token budget is our fault.
//!
//! Nothing here writes to the research benchmark's artifacts; results land in `artifacts/`.
//!
//! ```text
//! cargo run -p dcvr-model-bench -- --stage a
//! cargo run -p dcvr-model-bench -- --stage b --configs "gpt-5.6-terra:medium,gpt-5.6-sol:low"
//! ```

use std::time::{Duration, Instant};

use dcvr_llm_client::{set_llm_tuning, LlmClient, LlmTuning, OpenAiLlmClient};
use dcvr_roslyn_client::{HttpRoslynAnalyzer, RoslynAnalyzer};
use secrecy::SecretString;

/// One model + effort pairing under test.
#[derive(Clone, Debug)]
struct Config {
    model: String,
    effort: String,
}

impl Config {
    fn label(&self) -> String {
        format!("{}:{}", self.model, self.effort)
    }
}

/// Everything measured for a single prompt against a single configuration.
#[derive(Default, Debug)]
struct Trial {
    schema_ok: bool,
    candidate_present: bool,
    truncated: bool,
    fenced: bool,
    guardrail_ok: bool,
    compiled_first_pass: bool,
    compiled_after_repair: bool,
    repairs: u32,
    entry_point_ok: bool,
    named_parts: u32,
    total_parts: u32,
    model_ms: u128,
    total_ms: u128,
    chars: usize,
    error: Option<String>,
}

/// The screening corpus. Structurally varied on purpose, and deliberately containing
/// subjects that appear NOWHERE in the system prompt or its examples — a model that only
/// does well on the shapes it was shown has not generalised (§18, §43).
const STAGE_A: &[&str] = &[
    "create a blue cube",
    "make a table with four chairs",
    "build a small castle with four towers and a gate",
    "create a windmill with sails and a stone base",
    "make a complete chess set on a board",
    "create a small airport with a runway and a control tower",
    "create an atom model with a nucleus and orbiting electrons",
    "build a bridge across a gap",
    "create a robot with a head, torso and two articulated arms",
    "make a garden with trees, benches and a pond",
];

/// The finalist corpus: everything above plus harder compositions — nested helper classes,
/// repeated geometry, animation, bounded physics, many named children (§37).
const STAGE_B_EXTRA: &[&str] = &[
    "generate a solar system model with a sun and planets that orbit",
    "create a medieval village with houses, a well and a market stall",
    "build a pirate ship with masts, sails and a crow's nest",
    "create a futuristic laboratory with consoles and specimen tanks",
    "create a museum exhibition with plinths and display cases",
    "build a playground with a slide, swings and a climbing frame",
    "create a space station with a central hub and radial modules",
    "create a classroom with rows of desks and a whiteboard",
    "make a monument with a stepped base and an inscribed column",
    "create a racing track with barriers and a start gantry",
    "build a lighthouse on rocks with a rotating lamp",
    "create a greenhouse with glass panels and planting beds",
    "make a clock tower whose hands move slowly",
    "create a fairground carousel that turns",
    "build a suspension bridge with towers and cables",
    "create a small farm with a barn, fences and animals",
    "make a temple with columns and a stepped roof",
    "create a city block with towers of different heights",
    "make a snowman with a hat and a scarf",
    "create a rocket on a launch pad with support gantries",
];

#[tokio::main(flavor = "current_thread")]
async fn main() {
    let args: Vec<String> = std::env::args().collect();
    let stage = arg(&args, "--stage").unwrap_or_else(|| "a".to_string());
    let analyzer_url = std::env::var("DCVR_ROSLYN_URL")
        .unwrap_or_else(|_| "http://127.0.0.1:5099/analyze".to_string());

    let key = match std::env::var("OPENAI_API_KEY") {
        Ok(k) if !k.trim().is_empty() => k,
        _ => {
            eprintln!("OPENAI_API_KEY is not set (source .env first). Nothing to benchmark.");
            std::process::exit(2);
        }
    };

    // Discovered empirically, not assumed: `max` is NOT an accepted reasoning_effort for
    // any of these models — the API rejects it and names `xhigh` as the ceiling. The
    // user-facing MAX preset therefore has to map to `xhigh` (§34).
    let configs: Vec<Config> = match arg(&args, "--configs") {
        Some(list) => list
            .split(',')
            .filter_map(|s| {
                let (m, e) = s.split_once(':')?;
                Some(Config {
                    model: m.trim().to_string(),
                    effort: e.trim().to_string(),
                })
            })
            .collect(),
        None => vec![
            Config {
                model: "gpt-5.6-luna".into(),
                effort: "low".into(),
            },
            Config {
                model: "gpt-5.6-luna".into(),
                effort: "medium".into(),
            },
            Config {
                model: "gpt-5.6-terra".into(),
                effort: "low".into(),
            },
            Config {
                model: "gpt-5.6-terra".into(),
                effort: "medium".into(),
            },
            Config {
                model: "gpt-5.6-sol".into(),
                effort: "low".into(),
            },
            Config {
                model: "gpt-5.6-sol".into(),
                effort: "medium".into(),
            },
            // The incumbent, so the comparison has a baseline rather than only relative
            // rankings among new models.
            Config {
                model: "gpt-5.4-nano".into(),
                effort: "low".into(),
            },
        ],
    };

    let prompts: Vec<&str> = if stage == "b" {
        STAGE_A
            .iter()
            .chain(STAGE_B_EXTRA.iter())
            .copied()
            .collect()
    } else {
        STAGE_A.to_vec()
    };

    eprintln!(
        "[bench] stage {} — {} configs x {} prompts = {} calls",
        stage,
        configs.len(),
        prompts.len(),
        configs.len() * prompts.len()
    );

    let roslyn = HttpRoslynAnalyzer::new(analyzer_url);
    let mut rows = Vec::new();

    for cfg in &configs {
        let client = OpenAiLlmClient::new(SecretString::from(key.clone()), cfg.model.clone());
        let mut trials = Vec::new();

        for (i, p) in prompts.iter().enumerate() {
            // Same lever the admin panel uses, so the request the benchmark makes is the
            // request production makes.
            set_llm_tuning(LlmTuning {
                model: cfg.model.clone(),
                reasoning_effort: cfg.effort.clone(),
                verbosity: "default".into(),
                // Generous on purpose: a candidate truncated by OUR budget is not a model
                // mistake, and scoring it as one would pick the wrong winner (§40).
                max_completion_tokens: 32000,
            });

            let t = run_trial(&client, &roslyn, p).await;
            eprintln!(
                "[bench] {:<22} {:>2}/{:<2} {:<52} first={} repaired={} {}ms",
                cfg.label(),
                i + 1,
                prompts.len(),
                &p[..p.len().min(52)],
                yn(t.compiled_first_pass),
                yn(t.compiled_after_repair),
                t.total_ms
            );
            trials.push(t);
        }
        rows.push((cfg.clone(), trials));
    }

    report(&rows, &stage);
}

async fn run_trial(client: &OpenAiLlmClient, roslyn: &HttpRoslynAnalyzer, prompt: &str) -> Trial {
    let mut t = Trial::default();
    let started = Instant::now();

    let m0 = Instant::now();
    let gen = match tokio::time::timeout(
        Duration::from_secs(180),
        client.generate_dual("bench", prompt),
    )
    .await
    {
        Ok(Ok(g)) => g,
        Ok(Err(e)) => {
            t.error = Some(format!("{e}"));
            t.total_ms = started.elapsed().as_millis();
            return t;
        }
        Err(_) => {
            t.error = Some("timeout".into());
            t.total_ms = started.elapsed().as_millis();
            return t;
        }
    };
    t.model_ms = m0.elapsed().as_millis();
    t.schema_ok = true;

    let Some(csharp) = gen.csharp_candidate else {
        t.total_ms = started.elapsed().as_millis();
        return t;
    };
    t.candidate_present = !csharp.trim().is_empty();
    t.chars = csharp.len();
    t.fenced = csharp.contains("```");
    // A candidate that stops mid-token is a budget problem, not a coding one.
    t.truncated = t.candidate_present && !csharp.trim_end().ends_with('}');

    // THE UNCHANGED GUARDRAIL. Any configuration that only succeeds by being let through
    // a weakened gate is not a winner (§25, §42, §44).
    let verdict = dcvr_csharp_policy::validate_csharp_freeform(&csharp);
    t.guardrail_ok = verdict.violations.is_empty();

    t.entry_point_ok = has_entry_point(&csharp);
    let (named, total) = count_names(&csharp);
    t.named_parts = named;
    t.total_parts = total;

    if !t.guardrail_ok {
        t.total_ms = started.elapsed().as_millis();
        return t;
    }

    // First-pass compile, recorded separately from anything repair achieves.
    let mut source = csharp.clone();
    match roslyn.compile(&source).await {
        Ok(c) if c.approved && c.assembly.is_some() => {
            t.compiled_first_pass = true;
            t.compiled_after_repair = true;
            t.total_ms = started.elapsed().as_millis();
            return t;
        }
        Ok(c) => {
            // Same bounded policy as production: at most two repairs, each re-validated in
            // full before it is allowed near the compiler.
            let mut diagnostics = c.diagnostics.join(" | ");
            for _ in 0..2 {
                t.repairs += 1;
                let Ok(fixed) = client.repair_csharp("bench", &source, &diagnostics).await else {
                    break;
                };
                if !dcvr_csharp_policy::validate_csharp_freeform(&fixed)
                    .violations
                    .is_empty()
                {
                    break; // repaired into a policy violation: refused, exactly as in production
                }
                source = fixed;
                match roslyn.compile(&source).await {
                    Ok(c2) if c2.approved && c2.assembly.is_some() => {
                        t.compiled_after_repair = true;
                        break;
                    }
                    Ok(c2) => diagnostics = c2.diagnostics.join(" | "),
                    Err(_) => break,
                }
            }
        }
        Err(e) => t.error = Some(format!("compile service: {e}")),
    }

    t.total_ms = started.elapsed().as_millis();
    t
}

/// The runtime instantiates the class named exactly `GeneratedBehaviour`. A candidate that
/// buries its setup in a differently-named or nested class compiles perfectly and then
/// does nothing on the headset — which is the failure this project already shipped once,
/// so it is measured rather than assumed.
fn has_entry_point(src: &str) -> bool {
    src.contains("class GeneratedBehaviour")
}

/// How many created objects got a name a person could say.
///
/// Counts `.name = "..."` assignments and rejects the ones that are only a primitive type
/// or a bare index — "Cube", "Part3", "DCVRGEN_Cube" are not things anyone says out loud,
/// and naming is what makes "remove the north west tower" possible at all.
fn count_names(src: &str) -> (u32, u32) {
    let mut named = 0;
    let mut total = 0;
    for (idx, _) in src.match_indices(".name") {
        let rest = &src[idx..];
        let Some(q0) = rest.find('"') else { continue };
        let Some(q1) = rest[q0 + 1..].find('"') else {
            continue;
        };
        let value = &rest[q0 + 1..q0 + 1 + q1];
        if value.is_empty() {
            continue;
        }
        total += 1;
        let bare = value.trim_start_matches("DCVRGEN_");
        let lowered = bare.to_lowercase();
        let generic = matches!(
            lowered
                .trim_end_matches(|c: char| c.is_ascii_digit())
                .trim(),
            "cube"
                | "sphere"
                | "capsule"
                | "cylinder"
                | "plane"
                | "quad"
                | "part"
                | "object"
                | "gameobject"
                | "item"
                | ""
        );
        if !generic {
            named += 1;
        }
    }
    (named, total)
}

fn report(rows: &[(Config, Vec<Trial>)], stage: &str) {
    println!();
    println!("{}", "=".repeat(132));
    println!(" DreamCodeVR+ model benchmark — stage {stage} (live API, unchanged guardrail, real compiler)");
    println!("{}", "=".repeat(132));
    println!(
        "{:<24} {:>3} {:>8} {:>9} {:>9} {:>7} {:>7} {:>7} {:>8} {:>8} {:>7}",
        "CONFIG",
        "N",
        "schema%",
        "1st-pass%",
        "repaired%",
        "entry%",
        "named%",
        "repairs",
        "p50 ms",
        "p95 ms",
        "chars"
    );
    println!("{}", "-".repeat(132));

    let mut json = Vec::new();
    for (cfg, trials) in rows {
        let n = trials.len().max(1) as f64;
        let pct = |c: usize| 100.0 * c as f64 / n;
        let schema = pct(trials
            .iter()
            .filter(|t| t.schema_ok && t.candidate_present)
            .count());
        let first = pct(trials.iter().filter(|t| t.compiled_first_pass).count());
        let fixed = pct(trials.iter().filter(|t| t.compiled_after_repair).count());
        let entry = pct(trials.iter().filter(|t| t.entry_point_ok).count());

        let total_parts: u32 = trials.iter().map(|t| t.total_parts).sum();
        let named_parts: u32 = trials.iter().map(|t| t.named_parts).sum();
        let naming = if total_parts == 0 {
            0.0
        } else {
            100.0 * named_parts as f64 / total_parts as f64
        };
        let repairs: f64 = trials.iter().map(|t| t.repairs as f64).sum::<f64>() / n;

        let mut ms: Vec<u128> = trials.iter().map(|t| t.total_ms).collect();
        ms.sort_unstable();
        let p50 = pctile(&ms, 0.50);
        let p95 = pctile(&ms, 0.95);
        let chars = trials.iter().map(|t| t.chars).sum::<usize>() / trials.len().max(1);

        println!(
            "{:<24} {:>3} {:>7.0}% {:>8.0}% {:>8.0}% {:>6.0}% {:>6.0}% {:>7.2} {:>8} {:>8} {:>7}",
            cfg.label(),
            trials.len(),
            schema,
            first,
            fixed,
            entry,
            naming,
            repairs,
            p50,
            p95,
            chars
        );

        json.push(serde_json::json!({
            "model": cfg.model, "effort": cfg.effort, "n": trials.len(),
            "schema_pct": schema, "first_pass_compile_pct": first,
            "post_repair_compile_pct": fixed, "entry_point_pct": entry,
            "naming_pct": naming, "avg_repairs": repairs,
            "p50_ms": p50, "p95_ms": p95, "avg_chars": chars,
            "guardrail_violations": trials.iter().filter(|t| !t.guardrail_ok).count(),
            "truncated": trials.iter().filter(|t| t.truncated).count(),
            "fenced": trials.iter().filter(|t| t.fenced).count(),
            "errors": trials.iter().filter(|t| t.error.is_some()).count(),
        }));
    }
    println!("{}", "=".repeat(132));

    let dir = std::path::Path::new("artifacts");
    let _ = std::fs::create_dir_all(dir);
    let path = dir.join(format!("model-benchmark-stage-{stage}.json"));
    if let Ok(s) = serde_json::to_string_pretty(&serde_json::json!({
        "stage": stage,
        "note": "engineering evaluation of live models; SEPARATE from the deterministic research benchmark in apps/xr-security-eval",
        "guardrail": "unchanged (dcvr-csharp-policy validate_csharp_freeform)",
        "configs": json,
    })) {
        let _ = std::fs::write(&path, s);
        println!(" wrote {}", path.display());
    }
}

fn pctile(sorted: &[u128], q: f64) -> u128 {
    if sorted.is_empty() {
        return 0;
    }
    let i = ((sorted.len() as f64 - 1.0) * q).round() as usize;
    sorted[i.min(sorted.len() - 1)]
}

fn yn(b: bool) -> &'static str {
    if b {
        "Y"
    } else {
        "n"
    }
}

fn arg(args: &[String], flag: &str) -> Option<String> {
    args.iter()
        .position(|a| a == flag)
        .and_then(|i| args.get(i + 1))
        .cloned()
}
