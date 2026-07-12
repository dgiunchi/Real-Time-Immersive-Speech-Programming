/// A Ubiq network identifier: two little-endian `u32` components `{a, b}`.
///
/// DreamCodeVR uses the `b` component as the logical channel id; `a` is `0`
/// for the fixed service channels below. Kept `Copy` + `Hash` so it can be
/// used as a routing key without allocation.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash)]
pub struct NetworkId {
    pub a: u32,
    pub b: u32,
}

impl NetworkId {
    pub const fn new(a: u32, b: u32) -> Self {
        Self { a, b }
    }

    /// Construct from a single logical channel id (`a = 0`).
    pub const fn from_channel(b: u32) -> Self {
        Self { a: 0, b }
    }
}

/// Audio / STT input channel (Unity -> backend). In Phase 1 the payload body
/// carries a mock UTF-8 transcript; real PCM audio + STT arrive in Phase 2.
pub const NID_AUDIO_INPUT: NetworkId = NetworkId::new(0, 98);

/// Selected-object context channel (Unity -> backend): `objectName:materialName`.
pub const NID_SELECTED_OBJECT: NetworkId = NetworkId::new(0, 93);

/// Backend output channel (backend -> Unity): the validated decision/action-plan.
pub const NID_BACKEND_OUTPUT: NetworkId = NetworkId::new(0, 94);
