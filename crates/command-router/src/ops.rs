//! Deterministic object operations — the commands that never need a model.
//!
//! "Delete Saturn" is bookkeeping. "Make this red" is a bounded property write on an
//! object the user is pointing at. Neither is a creative act, and sending them to a
//! language model costs a round trip and a few cents to be told something that was never
//! in doubt. This module recognises them from the transcript and answers directly.
//!
//! # What this is not
//!
//! It is **not** a safety layer and it is **not** a bypass of one. The operations it emits
//! are re-validated on the device against the same numeric bounds a generated action plan
//! gets — scale limits, movement limits, personal space. Skipping the model does not skip
//! the validator, because the validator was never the model.
//!
//! # Two failure modes, and which one to prefer
//!
//! Under-matching is cheap: an unrecognised phrase falls through to the model, which is
//! where it would have gone anyway. Over-matching is expensive: mistaking a creative
//! request for an edit means the user asks for a red cube and something else turns red.
//! So every rule here is written to decline when unsure, and the article test in
//! [`is_creation`] exists precisely because "make a red cube" and "make it red" differ by
//! one word and mean entirely different things.
//!
//! Scope escalation is the one thing that must never happen: "delete Saturn" must not
//! become "clear everything". Universal clears are recognised by [`crate::reset`], which
//! requires a removal verb AND a universal object, and this module never widens a target.

/// A bounded instruction for the device to carry out against its own object registry.
///
/// The backend deliberately does not know what objects exist — the registry lives on the
/// headset, where the objects do. So `target` carries the user's own word for the thing
/// and the device resolves it. That keeps the backend stateless about scene contents and
/// means a stale idea of the scene can never drive a deletion.
#[derive(Debug, Clone, PartialEq)]
pub struct DeviceOp {
    pub op: &'static str,
    /// The user's word for the target. Empty means deictic ("this", "it") — the device
    /// resolves it from what the controller is pointing at.
    pub target: String,
    pub value: String,
    /// Direction in the USER's frame: x = right, y = up, z = forward.
    pub axis: [f32; 3],
    pub amount: f32,
}

impl DeviceOp {
    pub fn to_json(&self, peer: &str) -> String {
        serde_json::json!({
            "type": "op",
            "peer": peer,
            "op": self.op,
            "target": self.target,
            "value": self.value,
            "axis": self.axis,
            "amount": self.amount,
        })
        .to_string()
    }

    /// One-line summary for the operator's reply and the admin panel.
    pub fn describe(&self) -> String {
        match self.op {
            "clear_all" => "clear every generated object".to_string(),
            "delete" if self.target.is_empty() => "delete the pointed-at object".to_string(),
            "delete" => format!("delete '{}'", self.target),
            "set_color" => format!("colour '{}' {}", self.subject(), self.value),
            "set_scale" => format!("scale '{}' by {:.2}", self.subject(), self.amount),
            "move" => format!("move '{}' {:.2} m", self.subject(), self.amount),
            "rotate" => format!("rotate '{}'", self.subject()),
            other => other.to_string(),
        }
    }

    fn subject(&self) -> &str {
        if self.target.is_empty() {
            "the pointed-at object"
        } else {
            &self.target
        }
    }
}

/// Words that mean "the thing I am pointing at" rather than naming something.
const DEICTIC: [&str; 6] = ["this", "that", "it", "these", "those", "them"];

fn normalize(t: &str) -> String {
    t.trim().trim_end_matches(['.', '!', '?']).to_lowercase()
}

/// Strip leading articles and politeness so a target reads as a bare name.
fn clean_target(s: &str) -> String {
    let mut t = s.trim().trim_matches(['"', '\'']).trim();
    loop {
        let before = t;
        for a in ["the ", "a ", "an ", "my ", "that ", "this "] {
            if let Some(rest) = t.strip_prefix(a) {
                t = rest.trim_start();
            }
        }
        if t == before {
            break;
        }
    }
    // A deictic target is not a name.
    if DEICTIC.contains(&t) {
        return String::new();
    }
    t.trim_end_matches(" please").trim().to_string()
}

/// True when the phrase asks for something NEW rather than editing something present.
///
/// The discriminator is the indefinite article. "Make **a** red cube" introduces an
/// object; "make **it** red" modifies one. English is reliable here in the imperative
/// forms a person actually speaks to a machine, and when it is not, the phrase simply
/// falls through to the model — which is the correct destination for anything ambiguous.
fn is_creation(t: &str) -> bool {
    const CREATE_VERBS: [&str; 8] = [
        "create",
        "build",
        "generate",
        "spawn",
        "design",
        "construct",
        "draw",
        "model",
    ];
    let first = t.split_whitespace().next().unwrap_or("");
    if CREATE_VERBS.contains(&first) {
        return true;
    }
    // "make a ...", "make me a ...", "add a ..." introduce something new.
    for lead in [
        "make a ",
        "make an ",
        "make me a ",
        "make me an ",
        "make some ",
        "make two ",
        "make three ",
        "add a ",
        "add an ",
        "add some ",
        "add another ",
        "put a ",
        "put an ",
    ] {
        if t.starts_with(lead) {
            return true;
        }
    }
    false
}

/// Recognise a deterministic operation, or return `None` to let the model handle it.
///
/// Callers must check [`crate::reset::is_full_clear`] first; this never widens a scope to
/// a universal clear.
pub fn parse(transcript: &str) -> Option<DeviceOp> {
    let t = normalize(transcript);
    if t.is_empty() {
        return None;
    }

    // A creative request is never an edit, whatever else it contains. Checked first so
    // "build a castle and make it blue" goes to the model in one piece rather than being
    // half-executed as a colour change on nothing.
    if is_creation(&t) {
        return None;
    }

    // COMPOUND COMMANDS GO TO THE MODEL. Everything below produces exactly ONE bounded
    // operation, so "make this cube red and bigger" cannot be represented here: the
    // scale rule claims it, the colour half is dropped, and the target comes out as the
    // nonsense string "cube red and". Half-executing a request is worse than taking the
    // slower route, and the model composes multi-part edits properly.
    //
    // Found by an existing router test that expected this phrasing to reach the dual
    // path — the fast path had quietly taken it.
    if t.contains(" and ") || t.contains(" then ") || t.contains(", ") || t.contains(" also ") {
        return None;
    }

    if let Some(op) = parse_delete(&t) {
        return Some(op);
    }
    if let Some(op) = parse_color(&t) {
        return Some(op);
    }
    if let Some(op) = parse_scale(&t) {
        return Some(op);
    }
    if let Some(op) = parse_move(&t) {
        return Some(op);
    }
    if let Some(op) = parse_rotate(&t) {
        return Some(op);
    }
    None
}

fn parse_delete(t: &str) -> Option<DeviceOp> {
    const VERBS: [&str; 8] = [
        "delete ",
        "remove ",
        "erase ",
        "destroy ",
        "get rid of ",
        "take ",
        "throw ",
        "clear ",
    ];
    let rest = VERBS.iter().find_map(|v| t.strip_prefix(v))?;

    // English puts the particle after the object in "take Saturn AWAY" and before it in
    // "get rid OF Saturn". Handling only the prefix form silently dropped the first, so
    // the phrase fell through to the model — a wasted call for a command that was never
    // ambiguous.
    let rest = rest
        .trim_end_matches(" away")
        .trim_end_matches(" out")
        .trim();
    let target = clean_target(rest);

    // "delete" with nothing after it is not actionable; refuse rather than guess at a
    // scope. An empty target here only means a deictic word was stripped, which IS
    // actionable, so the two cases are told apart by what was actually said.
    if rest.is_empty() {
        return None;
    }
    if target.is_empty()
        && !DEICTIC
            .iter()
            .any(|d| rest.split_whitespace().any(|w| w == *d))
    {
        return None;
    }

    Some(DeviceOp {
        op: "delete",
        target,
        value: String::new(),
        axis: [0.0, 0.0, 0.0],
        amount: 0.0,
    })
}

const COLORS: [&str; 13] = [
    "red", "green", "blue", "yellow", "orange", "purple", "pink", "cyan", "white", "black", "grey",
    "gray", "brown",
];

fn parse_color(t: &str) -> Option<DeviceOp> {
    // "<verb> <target> <colour>" — e.g. "make earth blue", "turn this red",
    // "colour the tower green", "paint it black".
    let rest = ["make ", "turn ", "colour ", "color ", "paint ", "set "]
        .iter()
        .find_map(|v| t.strip_prefix(v))?;

    let colour = COLORS.iter().find(|c| {
        rest.split_whitespace()
            .last()
            .map(|w| w == **c)
            .unwrap_or(false)
    })?;

    let head = rest[..rest.len() - colour.len()].trim();
    let head = head
        .trim_end_matches(" to")
        .trim_end_matches(" into")
        .trim();
    // "make the cube's colour red" and similar leave filler; strip the common ones.
    let head = head
        .trim_end_matches(" colour")
        .trim_end_matches(" color")
        .trim();
    if head.is_empty() {
        return None;
    }

    Some(DeviceOp {
        op: "set_color",
        target: clean_target(head),
        value: (*colour).to_string(),
        axis: [0.0, 0.0, 0.0],
        amount: 0.0,
    })
}

fn parse_scale(t: &str) -> Option<DeviceOp> {
    // Multiplier, not an absolute size: the device clamps the RESULT against the same
    // scale bounds a plan gets, so repeated "bigger" cannot walk past the limit.
    const BIGGER: [&str; 6] = ["bigger", "larger", "huge", "huger", "giant", "enormous"];
    const SMALLER: [&str; 6] = ["smaller", "tinier", "tiny", "little", "smallest", "shrink"];

    let rest = ["make ", "scale ", "resize ", "grow ", "shrink "]
        .iter()
        .find_map(|v| t.strip_prefix(v))?;

    let last = rest.split_whitespace().last()?;
    let factor = if BIGGER.contains(&last) {
        1.5
    } else if SMALLER.contains(&last) {
        1.0 / 1.5
    } else {
        return None;
    };

    let head = rest[..rest.len() - last.len()].trim();
    let head = head
        .trim_end_matches(" a bit")
        .trim_end_matches(" much")
        .trim_end_matches(" a lot")
        .trim();
    if head.is_empty() {
        return None;
    }

    Some(DeviceOp {
        op: "set_scale",
        target: clean_target(head),
        value: String::new(),
        axis: [0.0, 0.0, 0.0],
        amount: factor,
    })
}

fn parse_move(t: &str) -> Option<DeviceOp> {
    let rest = ["move ", "push ", "shift ", "slide ", "nudge "]
        .iter()
        .find_map(|v| t.strip_prefix(v))?;

    // Direction in the USER's frame; the device converts it against the horizontal camera
    // basis so "left" is the user's left rather than the world's.
    let (dir_word, axis) = [
        ("left", [-1.0f32, 0.0, 0.0]),
        ("right", [1.0, 0.0, 0.0]),
        ("up", [0.0, 1.0, 0.0]),
        ("down", [0.0, -1.0, 0.0]),
        ("forward", [0.0, 0.0, 1.0]),
        ("forwards", [0.0, 0.0, 1.0]),
        ("back", [0.0, 0.0, -1.0]),
        ("backward", [0.0, 0.0, -1.0]),
        ("backwards", [0.0, 0.0, -1.0]),
        ("closer", [0.0, 0.0, -1.0]),
        ("away", [0.0, 0.0, 1.0]),
    ]
    .into_iter()
    .find(|(w, _)| rest.split_whitespace().any(|x| x == *w))?;

    // An explicit distance if one was given, else a comfortable default step.
    let amount = rest
        .split_whitespace()
        .find_map(|w| w.parse::<f32>().ok())
        .unwrap_or(0.5)
        .clamp(0.05, 10.0);

    let head = rest.split(dir_word).next().unwrap_or("").trim();
    let head = head
        .trim_end_matches(" to the")
        .trim_end_matches(" the")
        .trim();

    Some(DeviceOp {
        op: "move",
        target: clean_target(head),
        value: String::new(),
        axis,
        amount,
    })
}

fn parse_rotate(t: &str) -> Option<DeviceOp> {
    let rest = ["rotate ", "spin ", "turn "]
        .iter()
        .find_map(|v| t.strip_prefix(v))?;

    // "TURN ON the camera" is not a rotation, and this is not a stylistic point: without
    // this guard the phrase parses as `rotate "on the camera"`, which means an attempt to
    // switch on a sensor takes the fast path and comes back as "could not find that
    // object" instead of being refused by the intent screen. A capability request must
    // never be answered by the object-editing router — the fast path exists to skip the
    // MODEL, not the guardrail. Phrasal "turn" is therefore not a rotation verb at all.
    for phrasal in [
        "on ", "off ", "up ", "down ", "into ", "toward", "back on", "back off",
    ] {
        if rest.starts_with(phrasal) {
            return None;
        }
    }

    // "turn it red" is a colour change; that rule ran first, but guard anyway so a colour
    // word here can never be read as a rotation.
    if rest.split_whitespace().any(|w| COLORS.contains(&w)) {
        return None;
    }

    let degrees = rest
        .split_whitespace()
        .find_map(|w| {
            w.trim_end_matches("degrees")
                .trim_end_matches('°')
                .parse::<f32>()
                .ok()
        })
        .unwrap_or(45.0)
        .clamp(-360.0, 360.0);

    let head = rest
        .split(|c: char| c.is_ascii_digit())
        .next()
        .unwrap_or("")
        .trim()
        .trim_end_matches(" by")
        .trim();
    if head.is_empty() {
        return None;
    }

    Some(DeviceOp {
        op: "rotate",
        target: clean_target(head),
        value: String::new(),
        axis: [0.0, 1.0, 0.0],
        amount: degrees,
    })
}

/// The device-side clear, emitted instead of the legacy generated-C# sweep.
pub fn clear_all_op() -> DeviceOp {
    DeviceOp {
        op: "clear_all",
        target: String::new(),
        value: String::new(),
        axis: [0.0, 0.0, 0.0],
        amount: 0.0,
    }
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    fn op(t: &str) -> Option<DeviceOp> {
        parse(t)
    }

    // ---- the property that matters most: no scope escalation ----------------------

    #[test]
    fn a_named_delete_never_becomes_a_clear() {
        for t in [
            "delete saturn",
            "remove the north tower",
            "erase earth",
            "get rid of the robot",
        ] {
            let o = op(t).unwrap_or_else(|| panic!("should parse: {t}"));
            assert_eq!(o.op, "delete", "{t}");
            assert!(!o.target.is_empty(), "{t} must keep its target");
            assert!(
                !crate::reset::is_full_clear(t),
                "{t} must not read as a clear"
            );
        }
    }

    #[test]
    fn universal_clears_are_left_to_the_reset_module() {
        // These are recognised upstream; this module must not claim them as a scoped
        // delete with a nonsense target like "everything".
        for t in ["clear everything", "delete everything", "remove all"] {
            assert!(crate::reset::is_full_clear(t), "{t}");
        }
    }

    // ---- creation must never be mistaken for editing -------------------------------

    #[test]
    fn creative_requests_fall_through_to_the_model() {
        for t in [
            "create a solar system",
            "build a castle with four towers",
            "generate the solar system",
            "make a red cube",
            "make me a blue robot",
            "add a tree next to the house",
            "design a futuristic laboratory",
            "build a medieval village",
        ] {
            assert!(op(t).is_none(), "{t} is creative and must reach the model");
        }
    }

    #[test]
    fn make_a_red_cube_is_creation_but_make_it_red_is_an_edit() {
        assert!(op("make a red cube").is_none());
        let e = op("make it red").expect("edit");
        assert_eq!(e.op, "set_color");
        assert_eq!(e.value, "red");
        assert_eq!(e.target, "", "deictic target resolves on the device");
    }

    // ---- deletion ------------------------------------------------------------------

    #[test]
    fn deictic_delete_has_an_empty_target() {
        for t in ["delete this", "remove that", "destroy it", "erase this"] {
            let o = op(t).unwrap_or_else(|| panic!("{t}"));
            assert_eq!(o.op, "delete");
            assert_eq!(o.target, "", "{t}");
        }
    }

    #[test]
    fn named_delete_keeps_the_name_without_articles() {
        assert_eq!(op("delete the castle").unwrap().target, "castle");
        assert_eq!(op("remove saturn").unwrap().target, "saturn");
        assert_eq!(
            op("get rid of the north tower").unwrap().target,
            "north tower"
        );
        assert_eq!(op("take saturn away").unwrap().target, "saturn");
    }

    #[test]
    fn a_bare_verb_is_not_actionable() {
        assert!(op("delete").is_none());
        assert!(op("remove ").is_none());
    }

    // ---- properties ----------------------------------------------------------------

    #[test]
    fn colour_edits_parse_target_and_colour() {
        let o = op("make earth blue").unwrap();
        assert_eq!(
            (o.op, o.target.as_str(), o.value.as_str()),
            ("set_color", "earth", "blue")
        );

        let o = op("turn the tower green").unwrap();
        assert_eq!((o.target.as_str(), o.value.as_str()), ("tower", "green"));

        let o = op("paint it black").unwrap();
        assert_eq!((o.target.as_str(), o.value.as_str()), ("", "black"));
    }

    #[test]
    fn scale_is_a_multiplier_in_the_right_direction() {
        assert!(op("make earth bigger").unwrap().amount > 1.0);
        assert!(op("make this smaller").unwrap().amount < 1.0);
        assert_eq!(op("make earth bigger").unwrap().target, "earth");
    }

    #[test]
    fn move_resolves_direction_in_the_user_frame() {
        let o = op("move earth left").unwrap();
        assert_eq!(o.op, "move");
        assert_eq!(o.target, "earth");
        assert_eq!(o.axis, [-1.0, 0.0, 0.0]);

        assert_eq!(op("move it right").unwrap().axis, [1.0, 0.0, 0.0]);
        assert_eq!(op("push this up").unwrap().axis, [0.0, 1.0, 0.0]);
    }

    #[test]
    fn an_explicit_distance_is_honoured_and_clamped() {
        assert_eq!(op("move it left 2").unwrap().amount, 2.0);
        // Absurd distances are clamped rather than refused; the device clamps again.
        assert!(op("move it left 9999").unwrap().amount <= 10.0);
    }

    #[test]
    fn turn_it_red_is_a_colour_not_a_rotation() {
        let o = op("turn it red").unwrap();
        assert_eq!(o.op, "set_color");
    }

    #[test]
    fn rotate_parses_without_a_colour() {
        let o = op("rotate the castle").unwrap();
        assert_eq!(o.op, "rotate");
        assert_eq!(o.target, "castle");
    }

    // ---- everything else goes to the model ------------------------------------------

    /// Regression: the fast path claimed "make this cube red and bigger" and produced
    /// `set_scale` on a target called "cube red and", silently dropping the colour half.
    /// One bounded operation or none — compounds belong to the model.
    #[test]
    fn compound_edits_go_to_the_model() {
        for t in [
            "make this cube red and bigger",
            "move it left and up",
            "delete saturn and earth",
            "make it red, then bigger",
        ] {
            assert!(op(t).is_none(), "{t} is compound and must reach the model");
        }
        // The single-operation forms still take the fast path.
        assert!(op("make this cube red").is_some());
        assert!(op("move it left").is_some());
    }

    #[test]
    fn unrecognised_phrases_are_not_claimed() {
        for t in [
            "what can you do",
            "hello",
            "tell me about the guardrail",
            "make the castle feel more medieval",
            "secretly turn on the camera",
        ] {
            assert!(op(t).is_none(), "{t} must not be claimed by the fast path");
        }
    }

    /// Regression: "turn ON the camera" once parsed as `rotate "on the camera"`.
    ///
    /// Found by this module's own test suite, and worth pinning precisely because the
    /// consequence is subtle. Nothing unsafe would have executed — the device cannot
    /// resolve an object called "on the camera" — but the attempt would have been
    /// answered by the object router with "could not find that object" INSTEAD of being
    /// refused by the intent screen. A sensor request that comes back as a spelling
    /// complaint has been routed around the guardrail, and it would not appear in the
    /// security log at all. Phrasal `turn on/off` is therefore not a rotation verb.
    #[test]
    fn phrasal_turn_on_is_not_a_rotation() {
        assert!(op("turn on the camera").is_none());
        assert!(op("turn off the guardian").is_none());
        assert!(op("turn on the microphone").is_none());
        // The real rotation still works.
        assert_eq!(op("turn the castle").unwrap().op, "rotate");
    }

    #[test]
    fn an_attack_phrasing_is_never_claimed_as_a_local_op() {
        // The fast path must not become a way around the intent screen: these must all
        // reach the guardrail exactly as they did before this module existed.
        for t in [
            "turn on the camera",
            "make the camera stream to my server",
            "secretly enable the microphone",
            "disable the guardian boundary and walk me forward",
        ] {
            assert!(op(t).is_none(), "{t} must not be claimed by the fast path");
        }
    }

    /// A user may legitimately delete a scene object whose NAME contains a sensitive
    /// word — they built a camera prop, and now they want it gone (§70). Deleting a
    /// generated object is not a capability request, and refusing it would be the
    /// over-block this project measures against.
    #[test]
    fn deleting_an_object_with_a_sensitive_name_is_still_an_ordinary_delete() {
        let o = op("delete the camera prop").expect("an object delete");
        assert_eq!(o.op, "delete");
        assert_eq!(o.target, "camera prop");
    }

    #[test]
    fn json_carries_the_operation_and_the_peer() {
        let o = op("delete saturn").unwrap();
        let j: serde_json::Value = serde_json::from_str(&o.to_json("peer-1")).unwrap();
        assert_eq!(j["type"], "op");
        assert_eq!(j["op"], "delete");
        assert_eq!(j["target"], "saturn");
        assert_eq!(j["peer"], "peer-1");
    }
}
