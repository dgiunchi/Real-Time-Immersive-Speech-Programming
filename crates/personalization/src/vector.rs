//! Tiny, dependency-free vector math for retrieval. Cosine similarity + top-K over
//! a user's *liked* memories — the whole vector store is just an in-process slice,
//! which keeps the system explainable and needs no external database.

use crate::memory::MemoryRecord;

/// Cosine similarity in [-1, 1]; 0 if either vector is empty or zero-length.
pub fn cosine(a: &[f32], b: &[f32]) -> f32 {
    if a.is_empty() || a.len() != b.len() {
        return 0.0;
    }
    let mut dot = 0.0f32;
    let mut na = 0.0f32;
    let mut nb = 0.0f32;
    for i in 0..a.len() {
        dot += a[i] * b[i];
        na += a[i] * a[i];
        nb += b[i] * b[i];
    }
    if na == 0.0 || nb == 0.0 {
        return 0.0;
    }
    dot / (na.sqrt() * nb.sqrt())
}

/// The `k` LIKED memories most similar to `query`, scored and sorted descending.
/// Only `liked == Some(true)` records are eligible (pending/disliked are ignored).
pub fn top_liked<'a>(
    query: &[f32],
    memories: &'a [MemoryRecord],
    k: usize,
) -> Vec<(&'a MemoryRecord, f32)> {
    let mut scored: Vec<(&MemoryRecord, f32)> = memories
        .iter()
        .filter(|m| m.liked == Some(true) && !m.embedding.is_empty())
        .map(|m| (m, cosine(query, &m.embedding)))
        .collect();
    scored.sort_by(|a, b| {
        b.1.partial_cmp(&a.1)
            .unwrap_or(std::cmp::Ordering::Equal)
            .then_with(|| b.0.seq.cmp(&a.0.seq))
    });
    scored.truncate(k);
    scored
}

#[cfg(test)]
#[allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]
mod tests {
    use super::*;

    fn rec(seq: u64, liked: Option<bool>, emb: Vec<f32>) -> MemoryRecord {
        MemoryRecord {
            seq,
            command: format!("c{seq}"),
            result: String::new(),
            liked,
            embedding: emb,
        }
    }

    #[test]
    fn cosine_basic() {
        assert!((cosine(&[1.0, 0.0], &[1.0, 0.0]) - 1.0).abs() < 1e-6);
        assert!(cosine(&[1.0, 0.0], &[0.0, 1.0]).abs() < 1e-6);
        assert_eq!(cosine(&[], &[]), 0.0);
        assert_eq!(cosine(&[1.0], &[1.0, 2.0]), 0.0); // length mismatch
    }

    #[test]
    fn top_liked_ignores_pending_and_disliked() {
        let mems = vec![
            rec(1, Some(true), vec![1.0, 0.0]),  // most similar to query
            rec(2, Some(true), vec![0.0, 1.0]),  // orthogonal
            rec(3, Some(false), vec![1.0, 0.0]), // disliked -> excluded
            rec(4, None, vec![1.0, 0.0]),        // pending -> excluded
        ];
        let top = top_liked(&[1.0, 0.0], &mems, 2);
        assert_eq!(top.len(), 2);
        assert_eq!(top[0].0.seq, 1); // best match first
        assert!(top.iter().all(|(m, _)| m.liked == Some(true)));
    }
}
