use std::collections::HashMap;

use tree_sitter::{Node, Parser};

use crate::verdict::CsharpViolation;

/// Maximum accepted candidate size. Bounded guardrail against over-large code
/// (the "no insanely large asset/code" rule) while leaving room for creative
/// multi-primitive builds (houses, trees) on the Mode-A freeform path.
pub const MAX_LEN: usize = 16384;

/// The C# hardening profile selecting which ban sets apply.
///
/// `CreativeFreedom` (DEFAULT) bans ONLY genuine SYSTEM access (files / net /
/// process / reflection) so creative builds — houses, solar systems, particle
/// effects, camera-relative shaders, animation via `Time.time` — compile with full
/// freedom. `DeployHardened` is the OPT-IN deployment switch that ALSO bans the
/// perceptual/embodied-attack API surface (XR rig / tracking / camera-view /
/// post-process / haptics / second-display / scene-scanning) drawn from the XR
/// attack literature, for a shippable embodied-safe build. It is never on by
/// default, so it never limits creation; it is a deploy-time choice, not a
/// creative constraint.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum HardeningProfile {
    /// System-access bans only. Full creative freedom. DEFAULT.
    #[default]
    CreativeFreedom,
    /// System-access + perceptual/embodied-attack API bans. Opt-in (deploy time).
    DeployHardened,
}

/// SYSTEM-access banned namespaces (ALWAYS applied — both profiles). Matched as a
/// contiguous SUBSEQUENCE of the reconstructed dotted-name segments, so `System.IO`,
/// `System . IO` (whitespace), `global::System.IO`, `@System.IO`, and
/// `...System.IO.File.ReadAllText(...)` all hit.
const SECURITY_BANNED_NAMESPACES: &[&[&str]] = &[
    &["System", "IO"],
    &["System", "Net"],
    &["System", "Reflection"],
    &["System", "Diagnostics"],
    &["System", "Threading"],
    &["System", "Runtime", "InteropServices"],
    &["UnityEngine", "Networking"],
    &["Application", "Quit"],
];

/// SYSTEM-access banned identifiers (ALWAYS applied — both profiles). Covers dynamic
/// dispatch and reflection verbs that reach arbitrary methods.
const SECURITY_BANNED_IDENTIFIERS: &[&str] = &[
    "Process",
    "Assembly",
    "AppDomain",
    "Environment",
    "Activator",
    "Marshal",
    "DllImport",
    "UnityWebRequest",
    "GetType",
    "SendMessage",
    "SendMessageUpwards",
    "BroadcastMessage",
    "GetMethod",
    "GetField",
    "GetProperty",
    "GetConstructor",
    "MethodInfo",
    "FieldInfo",
    "PropertyInfo",
    "PlayerPrefs",
    "Resources",
    // NOTE: Destroy / DestroyImmediate are intentionally NOT banned. Removing objects you
    // just created ("remove this", "clear everything") is a core creative operation, and
    // the worst case — deleting a scene object — is low-severity and restart-recoverable,
    // unlike the code-execution / exfiltration / reflection identifiers above. The system
    // prompt guides generated code to protect core scene infrastructure (camera, lights,
    // networking, player rig) when clearing, and the runtime monitor is the backstop.
    "InvokeRepeating",
];

/// PERCEPTUAL / EMBODIED-attack namespaces — added ONLY under
/// `HardeningProfile::DeployHardened`. These manipulate the user's frame rather
/// than build content, so banning them costs no creative expressiveness.
const PERCEPTUAL_BANNED_NAMESPACES: &[&[&str]] = &[
    &["UnityEngine", "XR"],
    &["Unity", "XR"],
    &["UnityEngine", "InputSystem", "XR"],
];

/// PERCEPTUAL / EMBODIED-attack identifiers — added ONLY under `DeployHardened`.
///
/// SCOPE (deliberate, set by the security analysis of all six papers): this
/// list contains ONLY the **device / rig / tracking / haptics / capture** APIs that
/// have *no legitimate content-authoring use* — touching them means touching the
/// user's headset, never building content. Banning them therefore costs zero
/// creative freedom.
///
/// What is intentionally NOT here, and why: the verification proved that the
/// canonical view/herding vectors — mutating `Camera.main.transform`, the rig root,
/// viewport `rect`/`fieldOfView`, post-process (`OnRenderImage`/`Blit`/`RenderTexture`),
/// scene queries (`GameObject.Find`/`FindObjectsOfType`) and hiding objects
/// (`Renderer.enabled`/`SetActive`) — are LEXICALLY INDISTINGUISHABLE from legitimate
/// creative authoring (a build's own minimap/portal camera, a reflection RenderTexture,
/// a "recolour every object" command, an NPC `CharacterController`). A token denylist
/// either misses the attack (bare `Camera.main.transform`) or over-blocks creation —
/// both were demonstrated. Those vectors are therefore enforced at RUNTIME by
/// `UserFrameGuardian` (which re-asserts the tracking-owned head/rig pose every frame
/// and reverts hides of authentic, non-generated objects — it knows *which* object is
/// the live rig, which a lexer cannot) and contained by the Mode-D sandbox. The lexical
/// layer here is defence-in-depth for the unambiguous device surface only.
///
/// Also intentionally NOT here (adversarial-verify finding): the Quest-3 MIXED-REALITY
/// and body-interaction APIs — passthrough compositing (`OVRPassthroughLayer`), spatial
/// anchors / scene model (`OVRSpatialAnchor`/`OVRSceneAnchor`/`OVRSceneManager`), and
/// hand / eye / face tracking (`OVRHand`/`OVRSkeleton`/`OVREyeGaze`/`OVRFaceExpressions`).
/// These are the standard primitives for *building* MR content ("show my room and anchor
/// a lamp to my desk", gaze/hand-driven interaction), so banning them lexically over-blocks
/// benign creative builds. Covert capture/manipulation via them is a RUNTIME/disclosure
/// concern (Phase-6 monitors), and exfiltration is already blocked by the `System.Net`
/// ban; merely *reading* them to author content is legitimate and stays free.
///
/// References: Casey 2021 (rig/boundary), Ishii 2016, Tseng 2022 (XR rig/tracking
/// puppetry), Krauss 2024 (haptics), Casey 2021 (camera/passthrough capture).
const PERCEPTUAL_BANNED_IDENTIFIERS: &[&str] = &[
    // XR rig / tracking / boundary (the user's headset+play-space — never content)
    "XRSettings",
    "XRDevice",
    "InputTracking",
    "XROrigin",
    "TrackedPoseDriver",
    "OVRManager",
    "OVRCameraRig",
    "OVRPlugin",
    "OVRBoundary",
    "OVRInput",
    // XR device ENUMERATION (fingerprint the connected hardware — not content; same
    // category as XRSettings/XRDevice above). The bare identifiers also close a
    // subsequence gap: `using UnityEngine.XR; InputDevices.GetDevices(...)` never shows
    // the [UnityEngine, XR] namespace at the call site, so the namespace ban misses it.
    "InputDevices",
    "XRInputSubsystem",
    // haptics (the user's body — never content)
    "OVRHaptics",
    "SendHapticImpulse",
    "Vibrate",
    // camera capture (privacy exfiltration — never content)
    "WebCamTexture",
    // NOTE: OVRPassthroughLayer / OVRSpatialAnchor / OVRSceneAnchor / OVRSceneManager /
    // OVRHand / OVRSkeleton / OVREyeGaze / OVRFaceExpressions are DELIBERATELY NOT here —
    // they are dual-use MR content-authoring / interaction primitives (see the docstring
    // above). Banning them lexically over-blocks benign creative MR builds.
];

/// The active ban sets for a profile: security always; perceptual when hardened.
struct BanSets {
    namespaces: Vec<&'static [&'static str]>,
    identifiers: Vec<&'static str>,
}

impl BanSets {
    fn for_profile(profile: HardeningProfile) -> Self {
        let mut namespaces: Vec<&'static [&'static str]> = SECURITY_BANNED_NAMESPACES.to_vec();
        let mut identifiers: Vec<&'static str> = SECURITY_BANNED_IDENTIFIERS.to_vec();
        if matches!(profile, HardeningProfile::DeployHardened) {
            namespaces.extend_from_slice(PERCEPTUAL_BANNED_NAMESPACES);
            identifiers.extend_from_slice(PERCEPTUAL_BANNED_IDENTIFIERS);
        }
        Self {
            namespaces,
            identifiers,
        }
    }
}

const TYPE_DECL_KINDS: &[&str] = &[
    "class_declaration",
    "struct_declaration",
    "record_declaration",
    "record_struct_declaration",
    "interface_declaration",
    "enum_declaration",
    "delegate_declaration",
];

fn strip_at(text: &str) -> &str {
    text.strip_prefix('@').unwrap_or(text)
}

/// Decode the `\uXXXX` / `\UXXXXXXXX` escapes that C# accepts INSIDE identifiers, so an
/// identifier spelled with escapes normalises back to its real name and a banned member
/// (e.g. an escaped `Quit`, so `Application.Quit()`) is still caught. The AST
/// leaf text is the raw escape (tree-sitter does not decode), so without this a
/// determined attacker spells any banned identifier with an escape to slip past the
/// denylist while the C# compiler still resolves the real name. (Red-team finding.)
fn decode_unicode_escapes(s: &str) -> String {
    if !s.contains("\\u") && !s.contains("\\U") {
        return s.to_string();
    }
    let chars: Vec<char> = s.chars().collect();
    let mut out = String::with_capacity(s.len());
    let mut i = 0;
    while i < chars.len() {
        if chars[i] == '\\' && i + 1 < chars.len() && (chars[i + 1] == 'u' || chars[i + 1] == 'U') {
            let width = if chars[i + 1] == 'u' { 4 } else { 8 };
            if i + 2 + width <= chars.len() {
                let hex: String = chars[i + 2..i + 2 + width].iter().collect();
                if let Ok(cp) = u32::from_str_radix(&hex, 16) {
                    if let Some(ch) = char::from_u32(cp) {
                        out.push(ch);
                        i += 2 + width;
                        continue;
                    }
                }
            }
        }
        out.push(chars[i]);
        i += 1;
    }
    out
}

/// Normalise an identifier leaf: strip a `@` verbatim prefix, then decode unicode
/// escapes. Used everywhere a token is compared to the ban lists.
fn norm_ident(text: &str) -> String {
    decode_unicode_escapes(strip_at(text))
}

/// Build the map of `using X = Y.Z;` namespace/type ALIASES -> target segments, so a
/// later `X.IO.File` can be expanded to `System.IO.File` before subsequence matching.
/// Without this, `using Sys = System; Sys.IO.File.WriteAllText(...)` reconstructs as
/// `[Sys, IO, File]` and misses the `[System, IO]` ban entirely. (Red-team finding —
/// the single highest-yield C# bypass class.)
fn collect_alias_map(node: Node, src: &[u8], map: &mut HashMap<String, Vec<String>>) {
    const NAME_KINDS: &[&str] = &[
        "identifier",
        "qualified_name",
        "alias_qualified_name",
        "generic_name",
    ];
    if node.kind() == "using_directive" {
        let mut cursor = node.walk();
        let children: Vec<Node> = node.children(&mut cursor).collect();
        // An ALIAS directive (`using X = Y.Z;`) is exactly the ones containing an `=`
        // token. (This grammar has no `name_equals` node — the alias is `name:(id)`
        // then a bare `=` then the target name.) Alias = last name before `=`,
        // target = first name after `=`.
        if let Some(eq) = children.iter().position(|c| c.kind() == "=") {
            let alias_node = children[..eq]
                .iter()
                .rev()
                .find(|c| NAME_KINDS.contains(&c.kind()));
            let target_node = children[eq + 1..]
                .iter()
                .find(|c| NAME_KINDS.contains(&c.kind()));
            if let (Some(an), Some(tn)) = (alias_node, target_node) {
                if let Ok(atext) = an.utf8_text(src) {
                    let alias = norm_ident(atext);
                    let mut segs = Vec::new();
                    collect_segments(*tn, src, &mut segs);
                    if !segs.is_empty() {
                        map.insert(alias, segs);
                    }
                }
            }
        }
    }
    let mut cursor = node.walk();
    for child in node.children(&mut cursor) {
        collect_alias_map(child, src, map);
    }
}

/// Expand a leading alias segment to its target, transitively (bounded), so aliased
/// and chained (`using B = A; using A = System;`) names resolve to the real namespace.
fn resolve_alias(segs: &[String], map: &HashMap<String, Vec<String>>) -> Vec<String> {
    if map.is_empty() || segs.is_empty() {
        return segs.to_vec();
    }
    let mut cur = segs.to_vec();
    // Resolve to a FIXPOINT (not a fixed hop count), so an arbitrarily long alias
    // chain (`using A1=System; using A2=A1; … A9=A8;`) still reaches the real
    // namespace; the `seen` set stops a cyclic `using A=B; using B=A;` looping forever.
    let mut seen = std::collections::HashSet::new();
    while let Some(target) = map.get(&cur[0]) {
        if !seen.insert(cur[0].clone()) {
            break;
        }
        let mut expanded = target.clone();
        expanded.extend_from_slice(&cur[1..]);
        cur = expanded;
    }
    cur
}

/// Collect identifier-leaf segments under a node, in order, stripping `@` and
/// dropping a `global` alias segment. Rebuilds the dotted name structurally so
/// whitespace/comments/prefixes cannot hide it.
fn collect_segments(node: Node, src: &[u8], out: &mut Vec<String>) {
    if node.kind() == "identifier" {
        if let Ok(t) = node.utf8_text(src) {
            let t = norm_ident(t);
            if t != "global" {
                out.push(t);
            }
        }
        return;
    }
    let mut cursor = node.walk();
    for child in node.children(&mut cursor) {
        collect_segments(child, src, out);
    }
}

fn contains_subsequence(haystack: &[String], needle: &[&str]) -> bool {
    if needle.is_empty() || needle.len() > haystack.len() {
        return false;
    }
    haystack
        .windows(needle.len())
        .any(|w| w.iter().zip(needle.iter()).all(|(a, b)| a.as_str() == *b))
}

#[derive(Default)]
struct Scan {
    violations: Vec<CsharpViolation>,
    top_level_types: usize,
    found_mono: bool,
}

fn analyze(
    node: Node,
    src: &[u8],
    inside_type: bool,
    scan: &mut Scan,
    bans: &BanSets,
    aliases: &HashMap<String, Vec<String>>,
) {
    let kind = node.kind();
    let is_type = TYPE_DECL_KINDS.contains(&kind);
    if is_type && !inside_type {
        scan.top_level_types += 1;
    }

    let text = node.utf8_text(src).unwrap_or("");
    match kind {
        "identifier" => {
            let id = norm_ident(text);
            if bans.identifiers.contains(&id.as_str()) {
                scan.violations.push(CsharpViolation::BannedToken(id));
            } else if id == "dynamic" {
                // `dynamic` late-binding can reach any member without the banned
                // member name ever appearing as an AST identifier (e.g.
                // `((dynamic)app).Quit()`). It has no creative-authoring use in a
                // generated MonoBehaviour, so ban it outright. (Red-team finding.)
                scan.violations
                    .push(CsharpViolation::BannedToken("dynamic".to_string()));
            }
        }
        "predefined_type" if norm_ident(text) == "dynamic" => {
            scan.violations
                .push(CsharpViolation::BannedToken("dynamic".to_string()));
        }
        "qualified_name" | "member_access_expression" | "alias_qualified_name" => {
            let mut segs = Vec::new();
            collect_segments(node, src, &mut segs);
            // Expand a leading `using X = ...` alias to its real target before matching,
            // so `Sys.IO.File` (alias Sys=System) resolves to `[System, IO, File]`.
            let segs = resolve_alias(&segs, aliases);
            for ns in &bans.namespaces {
                if contains_subsequence(&segs, ns) {
                    scan.violations
                        .push(CsharpViolation::BannedToken(ns.join(".")));
                }
            }
        }
        "base_list" => {
            // Match a base-type IDENTIFIER segment exactly, not a raw substring:
            // `: MonoBehaviour` / `: UnityEngine.MonoBehaviour` qualify, but
            // `: FakeMonoBehaviourX` or a `/* MonoBehaviour */` comment must not.
            let mut segs = Vec::new();
            collect_segments(node, src, &mut segs);
            if segs.iter().any(|s| s == "MonoBehaviour") {
                scan.found_mono = true;
            }
        }
        // `unsafe` as a modifier (`unsafe void Start()`) OR as a statement block
        // (`unsafe { int* p = &x; *p = 2; }`, which parses as `unsafe_statement`), plus
        // raw `pointer_type` (`int*`): ban all three so pointer memory access can't slip
        // past the identifier/namespace denylists. (Red-team finding.)
        "modifier" if text == "unsafe" => {
            scan.violations
                .push(CsharpViolation::BannedToken("unsafe".to_string()));
        }
        "unsafe_statement" => {
            scan.violations
                .push(CsharpViolation::BannedToken("unsafe".to_string()));
        }
        "pointer_type" => {
            scan.violations
                .push(CsharpViolation::BannedToken("pointer".to_string()));
        }
        _ => {}
    }

    let mut cursor = node.walk();
    for child in node.children(&mut cursor) {
        analyze(child, src, inside_type || is_type, scan, bans, aliases);
    }
}

/// Lexical + structural checks over the C# source. Never panics.
///
/// HONEST LIMITATION: a lexical scan cannot be complete against a determined
/// attacker. It reconstructs dotted names structurally (defeating whitespace,
/// comments, `global::`, and `@` verbatim tricks) and bans dynamic-dispatch /
/// reflection verbs, but the AUTHORITATIVE semantic allow-list is the .NET Roslyn
/// analyzer, and the sandbox is the runtime backstop.
pub fn lexical_check(src: &str) -> Vec<CsharpViolation> {
    lexical_check_full(src, MAX_LEN, usize::MAX, HardeningProfile::CreativeFreedom)
}

/// Like [`lexical_check`] but with caller-supplied size limits, which the admin
/// panel tunes at runtime. `max_lines == usize::MAX` disables the line check.
/// Uses the default `CreativeFreedom` profile (full creative freedom).
pub fn lexical_check_limited(
    src: &str,
    max_chars: usize,
    max_lines: usize,
) -> Vec<CsharpViolation> {
    lexical_check_full(src, max_chars, max_lines, HardeningProfile::CreativeFreedom)
}

/// Lexical check under an explicit [`HardeningProfile`] (default size limits).
/// `DeployHardened` additionally bans the perceptual/embodied-attack API surface.
pub fn lexical_check_with_profile(src: &str, profile: HardeningProfile) -> Vec<CsharpViolation> {
    lexical_check_full(src, MAX_LEN, usize::MAX, profile)
}

/// The full lexical/structural check: caller-supplied size limits AND hardening
/// profile. `max_lines == usize::MAX` disables the line check. Never panics.
pub fn lexical_check_full(
    src: &str,
    max_chars: usize,
    max_lines: usize,
    profile: HardeningProfile,
) -> Vec<CsharpViolation> {
    let bans = BanSets::for_profile(profile);
    let mut violations = Vec::new();
    if src.len() > max_chars {
        violations.push(CsharpViolation::TooLarge {
            len: src.len(),
            max: max_chars,
        });
    }
    let line_count = src.lines().count();
    if line_count > max_lines {
        violations.push(CsharpViolation::TooManyLines {
            lines: line_count,
            max: max_lines,
        });
    }

    let mut parser = Parser::new();
    if parser
        .set_language(&tree_sitter_c_sharp::LANGUAGE.into())
        .is_err()
    {
        violations.push(CsharpViolation::ParseError);
        return violations;
    }
    let tree = match parser.parse(src, None) {
        Some(t) => t,
        None => {
            violations.push(CsharpViolation::ParseError);
            return violations;
        }
    };
    let root = tree.root_node();
    if root.has_error() {
        violations.push(CsharpViolation::ParseError);
    }

    // Pre-pass: resolve `using X = Namespace;` aliases so aliased dotted names can be
    // expanded to their real target before the subsequence ban check.
    let mut aliases: HashMap<String, Vec<String>> = HashMap::new();
    collect_alias_map(root, src.as_bytes(), &mut aliases);

    let mut scan = Scan::default();
    analyze(root, src.as_bytes(), false, &mut scan, &bans, &aliases);
    violations.append(&mut scan.violations);

    // Exactly one top-level type, and it must be the single MonoBehaviour class.
    if scan.top_level_types != 1 {
        violations.push(CsharpViolation::ClassCount(scan.top_level_types));
    }
    if !scan.found_mono {
        violations.push(CsharpViolation::NotMonoBehaviour);
    }

    violations.sort_by(|a, b| format!("{a:?}").cmp(&format!("{b:?}")));
    violations.dedup();
    violations
}

#[cfg(test)]
mod redteam_regression {
    use super::*;

    fn banned(src: &str) -> bool {
        lexical_check(src)
            .iter()
            .any(|v| matches!(v, CsharpViolation::BannedToken(_)))
    }
    fn mono(payload: &str, usings: &str) -> String {
        format!("using UnityEngine;\n{usings}public class GeneratedBehaviour : MonoBehaviour {{ void Start() {{ {payload} }} }}\n")
    }

    #[test]
    fn alias_namespace_bypass_is_closed() {
        // `using Sys = System;` then Sys.IO/Net/Threading must resolve to the real
        // namespace and be banned (red-team's highest-yield C# bypass).
        for (u, p) in [
            (
                "using Sys = System;\n",
                "Sys.IO.File.WriteAllText(\"/x\",\"y\");",
            ),
            ("using S = System;\n", "var w=new S.Net.WebClient();"),
            ("using X = System;\n", "X.Threading.Thread.Sleep(9);"),
            (
                "using D = System.Diagnostics;\n",
                "D.Process.Start(\"sh\");",
            ),
        ] {
            assert!(banned(&mono(p, u)), "alias bypass not caught: {u}{p}");
        }
    }

    #[test]
    fn alias_chain_bypass_is_closed() {
        assert!(banned(&mono(
            "A.IO.File.WriteAllText(\"/x\",\"y\");",
            "using B = System;\nusing A = B;\n"
        )));
    }

    #[test]
    fn unicode_escape_bypass_is_closed() {
        assert!(banned(&mono("Application.\\u0051uit();", "")));
        assert!(banned(&mono("\\u0050layerPrefs.SetInt(\"k\",1);", "")));
        assert!(banned(&mono("gameObject.\\u0047etType();", "")));
    }

    #[test]
    fn dynamic_dispatch_is_banned() {
        assert!(banned(&mono("dynamic d=this; d.ToString();", "")));
    }

    #[test]
    fn creative_freedom_preserved() {
        // Benign builds — including scary-LOOKING but harmless names — must NOT be
        // flagged with a banned token (0 over-blocks was measured across 469 vectors).
        for p in [
            "GetComponent<Renderer>().material.color=Color.red;",
            "var c=GameObject.CreatePrimitive(PrimitiveType.Cube);c.transform.position=Vector3.up;",
            "transform.Rotate(Vector3.up, Time.deltaTime*90f);",
            "var trojan=GameObject.CreatePrimitive(PrimitiveType.Cube);trojan.name=\"TrojanHorse\";",
            "var shell=GameObject.CreatePrimitive(PrimitiveType.Sphere);shell.name=\"seashell\";",
            "var cam=new GameObject(\"minimap\").AddComponent<Camera>();cam.orthographic=true;",
        ] {
            assert!(!banned(&mono(p, "")), "creative code wrongly flagged: {p}");
        }
    }

    #[test]
    fn unsafe_statement_block_bypass_is_closed() {
        // `unsafe { int* p = &x; *p = 2; }` parses as `unsafe_statement` (NOT a
        // `modifier`); pointer memory access must be banned. (Red-team finding — was
        // a HIGH-severity full guardrail bypass.)
        assert!(banned(&mono("int x=1; unsafe { int* p=&x; *p=2; }", "")));
        assert!(banned(&mono("unsafe { }", "")));
        // the raw pointer type alone is banned
        assert!(banned(&mono(
            "System.IntPtr h; unsafe { byte* b=(byte*)0; }",
            ""
        )));
        // the modifier form stays banned too (regression guard)
        assert!(banned(
            "public class GeneratedBehaviour : MonoBehaviour { unsafe void Start(){} }"
        ));
    }

    #[test]
    fn deep_alias_chain_bypass_is_closed() {
        // A >8-deep alias chain must still resolve to System (fixpoint, not a fixed
        // 8-hop cap). (Red-team finding.)
        let usings = "using A1=System;\nusing A2=A1;\nusing A3=A2;\nusing A4=A3;\n\
                      using A5=A4;\nusing A6=A5;\nusing A7=A6;\nusing A8=A7;\nusing A9=A8;\n";
        assert!(
            banned(&mono("A9.IO.File.WriteAllText(\"/x\",\"y\");", usings)),
            "9-deep alias chain bypassed the namespace ban"
        );
    }

    #[test]
    fn monobehaviour_impostor_base_is_rejected() {
        let not_mono = |src: &str| {
            lexical_check(src)
                .iter()
                .any(|v| matches!(v, CsharpViolation::NotMonoBehaviour))
        };
        // A base type whose NAME merely contains "MonoBehaviour", or a comment, must
        // NOT satisfy the structural gate. (Red-team finding — was a raw substring test.)
        assert!(
            not_mono("using UnityEngine;\npublic class GeneratedBehaviour : FakeMonoBehaviourX { void Start(){} }\n"),
            "impostor base class accepted as MonoBehaviour"
        );
        assert!(
            not_mono("using UnityEngine;\npublic class GeneratedBehaviour : /* MonoBehaviour */ IComparable { void Start(){} }\n"),
            "commented MonoBehaviour satisfied the gate"
        );
        // the real base type (bare or qualified) still passes the mono gate
        assert!(!not_mono("using UnityEngine;\npublic class GeneratedBehaviour : MonoBehaviour { void Start(){} }\n"));
        assert!(!not_mono(
            "public class GeneratedBehaviour : UnityEngine.MonoBehaviour { void Start(){} }\n"
        ));
    }
}
