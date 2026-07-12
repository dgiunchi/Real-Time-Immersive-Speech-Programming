/// Raw push-to-talk audio. Defaults match the original Unity client: 16 kHz,
/// mono, signed 16-bit little-endian PCM.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct AudioUtterance {
    pub pcm_s16le: Vec<u8>,
    pub sample_rate: u32,
    pub channels: u16,
    pub bits_per_sample: u16,
}

impl AudioUtterance {
    pub fn new_16k_mono(pcm: Vec<u8>) -> Self {
        Self {
            pcm_s16le: pcm,
            sample_rate: 16_000,
            channels: 1,
            bits_per_sample: 16,
        }
    }

    /// Wrap the PCM in a 44-byte WAV container (for HTTP upload). Hand-built to
    /// avoid an extra dependency; deterministic and total.
    pub fn to_wav(&self) -> Vec<u8> {
        let channels = self.channels;
        let bits = self.bits_per_sample;
        let byte_rate = self.sample_rate * u32::from(channels) * (u32::from(bits) / 8);
        let block_align = channels * (bits / 8);
        let data_len = self.pcm_s16le.len() as u32;

        let mut w = Vec::with_capacity(44 + self.pcm_s16le.len());
        w.extend_from_slice(b"RIFF");
        w.extend_from_slice(&(36 + data_len).to_le_bytes());
        w.extend_from_slice(b"WAVE");
        w.extend_from_slice(b"fmt ");
        w.extend_from_slice(&16u32.to_le_bytes());
        w.extend_from_slice(&1u16.to_le_bytes()); // PCM
        w.extend_from_slice(&channels.to_le_bytes());
        w.extend_from_slice(&self.sample_rate.to_le_bytes());
        w.extend_from_slice(&byte_rate.to_le_bytes());
        w.extend_from_slice(&block_align.to_le_bytes());
        w.extend_from_slice(&bits.to_le_bytes());
        w.extend_from_slice(b"data");
        w.extend_from_slice(&data_len.to_le_bytes());
        w.extend_from_slice(&self.pcm_s16le);
        w
    }
}

/// A transcription result.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Transcript {
    pub text: String,
}
