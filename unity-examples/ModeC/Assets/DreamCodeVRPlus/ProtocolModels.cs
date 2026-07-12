namespace DreamCodeVRPlus
{
    /// <summary>
    /// Mirrors <c>dcvr-behaviour-dsl/src/bounds.rs</c>. The Rust backend is the
    /// authoritative validator; these values let the Unity client re-clamp
    /// defensively. KEEP IN SYNC with the Rust crate (a divergence test should be
    /// added in Phase 2).
    /// </summary>
    public static class ProtocolModels
    {
        public const string SupportedSchemaVersion = "1.0";

        public const int MaxActions = 16;
        public const int MaxSpawnCount = 8;
        public const int MaxTotalSpawnedPerSession = 64;
        public const int MaxHierarchyDepth = 3;

        public const float ScaleMin = 0.1f;
        public const float ScaleMax = 5.0f;
        public const float MoveSpeedMax = 5.0f;
        public const float MoveAmplitudeMax = 5.0f;
        public const float RotateDegPerSecMax = 360.0f;
        public const float PhysicsMassMin = 0.1f;
        public const float PhysicsMassMax = 100.0f;

        // Network IDs preserved from the original DreamCodeVR.
        public const int NidAudioInput = 98;
        public const int NidSelectedObject = 93;
        public const int NidBackendOutput = 94;
    }
}
