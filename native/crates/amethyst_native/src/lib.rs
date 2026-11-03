//! Amethyst's native plugin: a C ABI that Unity loads as `libamethyst_native.dylib`
//! (packages/com.amethyst.native, `NativeLib.cs`). Bump [`ABI_VERSION`] with any change to an
//! exported signature, on both sides.

/// The ABI version `NativeLib.AbiVersion` expects.
pub const ABI_VERSION: u32 = 1;

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
