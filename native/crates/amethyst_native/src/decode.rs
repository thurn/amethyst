//! Audio files decoded to interleaved stereo float samples by macOS's `afconvert`, for the music
//! player's `--file` playlists (ported from Playlist Survivors' `capture::decode`).

use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};

/// `path` as interleaved stereo float samples at `rate`.
pub fn stereo(path: &Path, rate: u32) -> Result<Vec<f32>, String> {
    let temp = Temp::new()?;
    let wav = temp.0.join("decoded.wav");
    let out = std::process::Command::new("/usr/bin/afconvert")
        .args(["-f", "WAVE", "-d", &format!("LEF32@{rate}"), "-c", "2"])
        .arg(path)
        .arg(&wav)
        .output()
        .map_err(|e| format!("afconvert: {e}"))?;
    if !out.status.success() {
        return Err(format!(
            "afconvert failed on {}: {}",
            path.display(),
            String::from_utf8_lossy(&out.stderr).trim()
        ));
    }
    let bytes = std::fs::read(&wav).map_err(|e| e.to_string())?;
    match wav_f32(&bytes) {
        Some(samples) if !samples.is_empty() => Ok(samples),
        Some(_) => Err(format!("empty audio: {}", path.display())),
        None => Err(format!(
            "afconvert wrote an unexpected WAV for {}",
            path.display()
        )),
    }
}

/// A temporary folder of each conversion's own, removed with it.
struct Temp(PathBuf);

impl Temp {
    fn new() -> Result<Temp, String> {
        static NEXT: AtomicU64 = AtomicU64::new(0);
        loop {
            let path = std::env::temp_dir().join(format!(
                "amethyst-decode-{}-{}",
                std::process::id(),
                NEXT.fetch_add(1, Ordering::Relaxed)
            ));
            match std::fs::create_dir(&path) {
                Ok(()) => return Ok(Temp(path)),
                Err(e) if e.kind() == std::io::ErrorKind::AlreadyExists => {}
                Err(e) => return Err(format!("{}: {e}", path.display())),
            }
        }
    }
}

impl Drop for Temp {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

/// The float32 samples of a WAV file's `data` chunk (to the file's end, if it is cut short), if
/// all are finite.
fn wav_f32(bytes: &[u8]) -> Option<Vec<f32>> {
    if bytes.get(..4)? != b"RIFF" || bytes.get(8..12)? != b"WAVE" {
        return None;
    }
    let mut at = 12;
    while at + 8 <= bytes.len() {
        let id = &bytes[at..at + 4];
        let len = u32::from_le_bytes(bytes[at + 4..at + 8].try_into().ok()?) as usize;
        let body = bytes.get(at + 8..(at + 8 + len).min(bytes.len()))?;
        if id == b"data" {
            let out: Vec<f32> = body
                .as_chunks::<4>()
                .0
                .iter()
                .map(|b| f32::from_le_bytes(*b))
                .collect();
            return out.iter().all(|v| v.is_finite()).then_some(out);
        }
        at += 8 + len + (len & 1);
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A mono float WAV at 8 kHz holding `n` samples of `value`.
    fn constant_wav(value: f32, n: u32) -> Vec<u8> {
        let mut bytes = b"RIFF".to_vec();
        bytes.extend((36 + n * 4).to_le_bytes());
        bytes.extend(b"WAVEfmt ");
        bytes.extend(16u32.to_le_bytes());
        bytes.extend(3u16.to_le_bytes());
        bytes.extend(1u16.to_le_bytes());
        bytes.extend(8000u32.to_le_bytes());
        bytes.extend(32000u32.to_le_bytes());
        bytes.extend(4u16.to_le_bytes());
        bytes.extend(32u16.to_le_bytes());
        bytes.extend(b"data");
        bytes.extend((n * 4).to_le_bytes());
        for _ in 0..n {
            bytes.extend(value.to_le_bytes());
        }
        bytes
    }

    #[test]
    fn a_file_decodes_to_stereo_at_the_asked_rate() {
        let dir = Temp::new().unwrap();
        let path = dir.0.join("tone.wav");
        std::fs::write(&path, constant_wav(0.25, 8000)).unwrap();
        let samples = stereo(&path, 16_000).unwrap();
        assert_eq!(samples.len(), 2 * 16_000);
        assert!((samples[16_000] - 0.25).abs() < 1e-3);
        assert!(stereo(&dir.0.join("missing.mp3"), 16_000).is_err());
        let temp = std::env::temp_dir();
        let leftovers = std::fs::read_dir(&temp).unwrap().filter(|e| {
            let name = e.as_ref().unwrap().file_name();
            let name = name.to_string_lossy();
            name.starts_with(&format!("amethyst-decode-{}-", std::process::id()))
                && *name != *dir.0.file_name().unwrap().to_string_lossy()
        });
        assert_eq!(
            leftovers.count(),
            0,
            "conversions must remove their folders"
        );
    }

    #[test]
    fn non_finite_or_malformed_wavs_are_refused() {
        let mut b = constant_wav(0.5, 2);
        assert_eq!(wav_f32(&b), Some(vec![0.5, 0.5]));
        let last = b.len() - 4;
        b[last..].copy_from_slice(&f32::NAN.to_le_bytes());
        assert_eq!(wav_f32(&b), None);
        assert_eq!(wav_f32(b"RIFF\0\0\0\0WAVX"), None);
    }
}
