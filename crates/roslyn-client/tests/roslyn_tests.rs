#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use dcvr_roslyn_client::{MockRoslynAnalyzer, RoslynAnalyzer};

#[tokio::test]
async fn mock_analyzer_approves_with_skip_note() {
    let v = MockRoslynAnalyzer.analyze("class X {}").await.unwrap();
    assert!(v.approved);
    assert!(v.diagnostics.iter().any(|d| d.contains("skipped")));
}
