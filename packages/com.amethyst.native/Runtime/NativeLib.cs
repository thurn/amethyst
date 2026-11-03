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
        public const uint AbiVersion = 1;

        [DllImport(Lib)]
        static extern uint amethyst_abi_version();

        [DllImport(Lib)]
        static extern float amethyst_hash01(ulong seed, int x, int z);

        static bool checkedVersion;

        /// <summary>A deterministic value in [0, 1) for a seed and a grid cell.</summary>
        public static float Hash01(ulong seed, int x, int z)
        {
            Check();
            return amethyst_hash01(seed, x, z);
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
