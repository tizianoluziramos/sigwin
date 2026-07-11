# Architecture

## Goals

Sigwin exists for one reason: **low memory footprint and fast cold start** versus the official
Electron desktop client. Every design choice favors native and lean.

- **Linked (secondary) device only.** Sigwin links to a phone by displaying a QR code the user
  scans, exactly like Signal Desktop. It is never the primary device and never registers a phone
  number itself.
- **Do not reimplement libsignal or the Signal protocol.** Reuse the Rust ecosystem.

## Components

### `core/` — Rust cdylib

- Built on [`presage`](https://github.com/whisperfish/presage), a high-level Rust client library
  that wraps the official Rust `libsignal`. presage provides: secondary-device linking, contact &
  profile sync from the primary device, groups, incoming/outgoing messages, and
  fetch/decrypt/store of attachments.
- Storage: [`presage-store-sqlite`](https://github.com/whisperfish/presage) — a `sqlx`/SQLite
  store with optional **sqlcipher** encryption. (This is presage's current and only supported
  store; the older `presage-store-sled` is deprecated.)
- Concurrency: a `tokio` multi-threaded runtime lives on a **background thread** owned by Rust.
  The UI thread never blocks on Signal work.
- FFI: a hand-written, clean **C ABI**. [`csbindgen`](https://github.com/Cysharp/csbindgen)
  auto-generates the C# P/Invoke layer into `bindings/`. Incoming messages and state changes are
  pushed to C# via **registered callback function pointers**, never by blocking a call.

> presage is consumed as a **git dependency pinned to a specific commit** — it is not published on
> crates.io (the `presage` crate on crates.io is an unrelated library). This is staged in
> `core/Cargo.toml` for Phase 1.

### `app/` — WinUI 3

- Windows App SDK 2.2, C#/XAML, MVVM via `CommunityToolkit.Mvvm`.
- Callbacks arriving from the Rust tokio thread are marshaled onto the UI thread with
  `DispatcherQueue` before touching any view model.
- Packaged (MSIX) app. User-facing branding is the neutral codename **Sigwin**.

### `bindings/` — generated C# P/Invoke

Produced by `csbindgen` during the Rust build. **Generated at build, not committed** (gitignored),
so the Rust `core/` must be built before the `app/` project that references it.

## Key build constraints (learned up front)

- **MSVC toolchain, not GNU.** The `boring`/BoringSSL dependency does not build under GNU on
  Windows. The Rust host triple must be `x86_64-pc-windows-msvc`.
- **libsignal MSRV ≈ 1.89**; a recent stable Rust satisfies it. (libsignal pins its own toolchain
  when built standalone, but as a *dependency* it builds with whatever toolchain invokes cargo, so
  we simply use a current stable.)
- **curve25519-dalek patch required.** presage/libsignal need the Signal fork. Staged in the
  workspace `Cargo.toml` for Phase 1:
  ```toml
  [patch.crates-io]
  curve25519-dalek = { git = "https://github.com/signalapp/curve25519-dalek", tag = "signal-curve25519-4.1.3" }
  ```
- Build tools: **protoc**, **CMake**, **NASM**, **libclang** must be present. See
  [setup.md](setup.md).

### The BoringSSL / OpenSSL question

libsignal statically builds **BoringSSL**; sqlcipher (via the SQLite store) pulls in **OpenSSL**.
Two crypto libraries in one binary can collide on symbols at link time on Windows — this is the
single most likely place the Phase 1 build breaks. Mitigations, in order of preference:

1. Prefer a **vendored OpenSSL** for sqlcipher (bundled + statically linked) so we don't depend on
   a system/vcpkg OpenSSL, and keep it isolated from BoringSSL.
2. If contact discovery (CDSI) is enabled later, presage documents needing a custom encryption
   provider to reconcile BoringSSL/OpenSSL — out of scope for the MVP.

**Phase 1's first milestone is therefore "does the core link at all," before we even attempt to
link to a phone.**

## Build phases

| Phase | Deliverable |
|------|-------------|
| **0** | Scaffold: repo layout, Cargo workspace, README + setup, toolchain preflight. ← *current* |
| **1** | Rust core headless: link via QR + receive, proven with the `sigwin-harness` CLI. |
| **2** | C ABI + csbindgen + a C# console app that links and prints incoming messages via callbacks. |
| **3** | WinUI 3 shell: linking screen (QR), conversation list, read-only thread view. |
| **4** | Send text. |
| **5** | Attachments: send images/audio; display received images; play received audio. |
| **6** | Emoji panel + polish. |

Out of scope for the MVP: calls, stories, group management UI beyond showing group messages,
disappearing-message timers, full settings. Each gets a clean `TODO` where it hooks in.
