using System;
using System.Runtime.InteropServices;

namespace Amethyst.Native
{
    /// <summary>
    /// The Rust plugin (native/crates/amethyst_native), which `just native` and every Unity
    /// step of tools/ build into this package's Plugins/macOS/.
    /// </summary>
    public static class NativeLib
    {
        const string Lib = "amethyst_native";

        /// <summary>The plugin's ABI version; bump both sides together.</summary>
        public const uint AbiVersion = 2;

        [DllImport(Lib)]
        static extern uint amethyst_abi_version();

        [DllImport(Lib)]
        static extern float amethyst_hash01(ulong seed, int x, int z);

        [DllImport(Lib)]
        static extern IntPtr amethyst_decode(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
            uint rate,
            out ulong len
        );

        [DllImport(Lib)]
        static extern void amethyst_free_samples(IntPtr samples, ulong len);

        [DllImport(Lib)]
        static extern IntPtr amethyst_last_error();

        static bool checkedVersion;

        /// <summary>A deterministic value in [0, 1) for a seed and a grid cell.</summary>
        public static float Hash01(ulong seed, int x, int z)
        {
            Check();
            return amethyst_hash01(seed, x, z);
        }

        /// <summary>
        /// The audio file at <paramref name="path"/> as interleaved stereo samples at
        /// <paramref name="rate"/>, decoded by macOS's afconvert (any format it reads: mp3, m4a,
        /// wav, aiff, flac...). Throws <see cref="InvalidOperationException"/> saying why it can't.
        /// Safe on any thread.
        /// </summary>
        public static float[] Decode(string path, int rate)
        {
            Check();
            var samples = amethyst_decode(path, (uint)rate, out var len);
            if (samples == IntPtr.Zero)
                throw new InvalidOperationException(Marshal.PtrToStringUTF8(amethyst_last_error()));
            try
            {
                var result = new float[len];
                Marshal.Copy(samples, result, 0, result.Length);
                return result;
            }
            finally
            {
                amethyst_free_samples(samples, len);
            }
        }

        static void Check()
        {
            if (checkedVersion)
                return;
            uint version;
            try
            {
                version = amethyst_abi_version();
            }
            catch (DllNotFoundException e)
            {
                throw new InvalidOperationException(
                    "the native plugin is missing: run `just native`",
                    e
                );
            }
            if (version != AbiVersion)
                throw new InvalidOperationException(
                    $"native plugin ABI {version}, expected {AbiVersion}: run `just native`"
                );
            checkedVersion = true;
        }
    }
}
