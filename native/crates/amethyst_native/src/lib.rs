//! Amethyst's native plugin: a C ABI that Unity loads as `libamethyst_native.dylib`
//! (packages/com.amethyst.native, `NativeLib.cs`). Bump [`ABI_VERSION`] with any change to an
//! exported signature, on both sides.

/// The ABI version `NativeLib.AbiVersion` expects.
pub const ABI_VERSION: u32 = 2;

mod decode;

use std::cell::RefCell;
use std::ffi::{CStr, CString, c_char};
use std::path::Path;

#[unsafe(no_mangle)]
pub extern "C" fn amethyst_abi_version() -> u32 {
    ABI_VERSION
}

/// A deterministic value in [0, 1) for a seed and a grid cell (`SplitMix64` of the inputs).
#[unsafe(no_mangle)]
pub extern "C" fn amethyst_hash01(seed: u64, x: i32, z: i32) -> f32 {
    let mut h = seed ^ (u64::from(x as u32) << 32 | u64::from(z as u32));
    h = h.wrapping_add(0x9E37_79B9_7F4A_7C15);
    h = (h ^ (h >> 30)).wrapping_mul(0xBF58_476D_1CE4_E5B9);
    h = (h ^ (h >> 27)).wrapping_mul(0x94D0_49BB_1331_11EB);
    h ^= h >> 31;
    (h >> 40) as f32 / (1u64 << 24) as f32
}

thread_local! {
    static LAST_ERROR: RefCell<CString> = RefCell::new(CString::default());
}

/// Decodes the audio file at `path` (UTF-8) to interleaved stereo float samples at `rate`. Returns
/// the samples, which [`amethyst_free_samples`] must free, and their count in `len`; or null, with
/// [`amethyst_last_error`] saying why on this thread.
///
/// # Safety
///
/// `path` must be a NUL-terminated string and `len` a valid pointer.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn amethyst_decode(
    path: *const c_char,
    rate: u32,
    len: *mut u64,
) -> *mut f32 {
    // SAFETY: the caller passes a NUL-terminated string.
    let path = unsafe { CStr::from_ptr(path) }
        .to_string_lossy()
        .into_owned();
    match decode::stereo(Path::new(&path), rate) {
        Ok(samples) => {
            let samples = samples.into_boxed_slice();
            // SAFETY: the caller passes a valid pointer.
            unsafe { *len = samples.len() as u64 };
            Box::into_raw(samples).cast::<f32>()
        }
        Err(e) => {
            LAST_ERROR.with(|last| {
                *last.borrow_mut() = CString::new(e.replace('\0', " ")).unwrap_or_default();
            });
            std::ptr::null_mut()
        }
    }
}

/// Frees samples [`amethyst_decode`] returned.
///
/// # Safety
///
/// `samples` and `len` must be what one [`amethyst_decode`] call returned, freed once.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn amethyst_free_samples(samples: *mut f32, len: u64) {
    if !samples.is_null() {
        // SAFETY: the caller passes a slice `amethyst_decode` boxed.
        drop(unsafe { Box::from_raw(std::ptr::slice_from_raw_parts_mut(samples, len as usize)) });
    }
}

/// Why this thread's last [`amethyst_decode`] failed, valid until its next call.
#[unsafe(no_mangle)]
pub extern "C" fn amethyst_last_error() -> *const c_char {
    LAST_ERROR.with(|last| last.borrow().as_ptr())
}

#[cfg(test)]
mod tests {
    use super::amethyst_hash01;

    #[test]
    fn hash_is_deterministic_and_in_range() {
        let values: Vec<f32> = (0..1000).map(|i| amethyst_hash01(7, i, -i)).collect();
        assert!(values.iter().all(|v| (0.0..1.0).contains(v)));
        assert_eq!(values[3].to_bits(), amethyst_hash01(7, 3, -3).to_bits());
        assert_ne!(
            amethyst_hash01(7, 3, -3).to_bits(),
            amethyst_hash01(8, 3, -3).to_bits()
        );
    }
}
