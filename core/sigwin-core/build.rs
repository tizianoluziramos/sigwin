//! Generates the C# P/Invoke bindings from this crate's `extern "C"` surface
//! using csbindgen, writing them into the repo's top-level `bindings/` folder.

use std::path::PathBuf;

fn main() {
    println!("cargo:rerun-if-changed=src/lib.rs");
    println!("cargo:rerun-if-changed=src/ffi.rs");

    // build.rs runs with CWD = this crate dir (core/sigwin-core), so ../../ is
    // the repo root.
    let out = PathBuf::from("../../bindings/NativeMethods.g.cs");

    csbindgen::Builder::default()
        .input_extern_file("src/lib.rs")
        .input_extern_file("src/ffi.rs")
        // cdylib on Windows produces `sigwin_core.dll`.
        .csharp_dll_name("sigwin_core")
        .csharp_namespace("Sigwin.Interop")
        .csharp_class_name("NativeMethods")
        .generate_csharp_file(&out)
        .expect("failed to generate C# bindings");
}
