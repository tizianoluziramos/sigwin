//! Sigwin core.
//!
//! Phase 1 implements the hard part headlessly: link as a secondary device and
//! receive messages, via [`signal`]. All presage/libsignal types are kept inside
//! this crate; callers interact through simple owned types and closures, which
//! Phase 2 turns into C-ABI function-pointer callbacks.

pub mod ffi;
pub mod signal;

use std::os::raw::c_int;

/// The C ABI version this core exposes. Bump when the C ABI changes so the C#
/// side can detect a mismatch.
const ABI_VERSION: i32 = 1;

/// Rust-native accessor used by the in-repo CLI harness.
pub fn abi_version() -> i32 {
    ABI_VERSION
}

/// C ABI: returns the core ABI version. Smoke-test export for csbindgen.
#[no_mangle]
pub extern "C" fn sigwin_abi_version() -> c_int {
    ABI_VERSION
}

// TODO(Phase 2): C ABI wrapping `signal::run` — runtime init/shutdown on a
// background tokio thread, store open, and function-pointer callbacks for the
// provisioning URL and incoming events.
