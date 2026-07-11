//! C ABI for the C# app.
//!
//! `sigwin_start` spawns a background thread that owns a tokio runtime and drives
//! [`crate::signal::run`]. Callbacks are invoked **from that background thread**,
//! so the C# side must marshal to its UI thread. Events are delivered as JSON
//! strings (serialized [`IncomingEvent`]). `sigwin_send_text` queues an outgoing
//! message onto the running core.

use std::ffi::{c_char, c_void, CStr, CString};
use std::sync::Once;
use std::thread::JoinHandle;

use futures::channel::{mpsc, oneshot};

use crate::signal::{self, Command, IncomingEvent};

static LOGGING_INIT: Once = Once::new();

/// Set up file logging next to the store (`<db_path>.log`), once per process.
/// The app build has no console, so without this every `tracing::*` call is
/// silently discarded — the only way today to see what the core is actually
/// doing (e.g. why a network call failed) is this file.
fn init_logging(db_path: &str) {
    LOGGING_INIT.call_once(|| {
        let log_path = format!("{db_path}.log");
        if let Ok(file) = std::fs::OpenOptions::new()
            .create(true)
            .append(true)
            .open(&log_path)
        {
            let _ = tracing_subscriber::fmt()
                .with_writer(std::sync::Mutex::new(file))
                .with_ansi(false)
                .with_env_filter("info")
                .try_init();
        }
    });
}

/// Called at most once, when linking is required, with the provisioning URI.
pub type UrlCallback = extern "C" fn(url: *const c_char, user_data: *mut c_void);

/// Called for each incoming event, with a JSON-encoded [`IncomingEvent`].
pub type EventCallback = extern "C" fn(event_json: *const c_char, user_data: *mut c_void);

/// Opaque handle owning the background worker thread.
pub struct SigwinHandle {
    worker: Option<JoinHandle<()>>,
    shutdown: Option<oneshot::Sender<()>>,
    commands: Option<mpsc::UnboundedSender<Command>>,
}

// Same generous stack as the CLI harness — libsignal overflows the default
// ~1 MB Windows main-thread stack.
const STACK_SIZE: usize = 16 * 1024 * 1024;

unsafe fn opt_string(p: *const c_char) -> Option<String> {
    if p.is_null() {
        return None;
    }
    CStr::from_ptr(p).to_str().ok().map(str::to_owned)
}

fn call_with_cstr(cb: extern "C" fn(*const c_char, *mut c_void), text: String, user_data: *mut c_void) {
    match CString::new(text) {
        Ok(c) => cb(c.as_ptr(), user_data),
        Err(_) => {
            let fallback = CString::new("{\"type\":\"Other\",\"description\":\"invalid string\"}").unwrap();
            cb(fallback.as_ptr(), user_data);
        }
    }
}

/// Start the core. Non-blocking; returns an opaque handle (or null on bad args).
///
/// # Safety
/// `db_path`, `passphrase`, `device_name` must be valid NUL-terminated UTF-8, or
/// null (only `passphrase` may be null). `user_data` is passed back verbatim to
/// the callbacks and must stay valid until [`sigwin_stop`] returns.
#[no_mangle]
pub unsafe extern "C" fn sigwin_start(
    db_path: *const c_char,
    passphrase: *const c_char,
    device_name: *const c_char,
    on_url: UrlCallback,
    on_event: EventCallback,
    user_data: *mut c_void,
) -> *mut SigwinHandle {
    let Some(db_path) = opt_string(db_path) else {
        return std::ptr::null_mut();
    };
    init_logging(&db_path);
    let passphrase = opt_string(passphrase);
    let device_name = opt_string(device_name).unwrap_or_else(|| "Sigwin".to_string());
    // Raw pointers aren't `Send`; move it across the thread boundary as a usize.
    let user = user_data as usize;
    let (shutdown_tx, shutdown_rx) = oneshot::channel::<()>();
    let (cmd_tx, cmd_rx) = mpsc::unbounded::<Command>();

    let worker = std::thread::Builder::new()
        .name("sigwin-core".into())
        .stack_size(STACK_SIZE)
        .spawn(move || {
            let rt = match tokio::runtime::Builder::new_multi_thread()
                .enable_all()
                .thread_stack_size(STACK_SIZE)
                .build()
            {
                Ok(rt) => rt,
                Err(e) => {
                    tracing::error!("failed to build tokio runtime: {e}");
                    return;
                }
            };

            rt.block_on(async move {
                let emit = |event: IncomingEvent| {
                    call_with_cstr(on_event, event_to_json(&event), user as *mut c_void);
                };

                tokio::select! {
                    result = signal::run(
                        &db_path,
                        passphrase.as_deref(),
                        &device_name,
                        |url| call_with_cstr(on_url, url, user as *mut c_void),
                        emit,
                        cmd_rx,
                    ) => {
                        if let Err(e) = result {
                            tracing::error!("sigwin core exited with error: {e:?}");
                        }
                    }
                    _ = shutdown_rx => {
                        tracing::info!("sigwin core shutdown requested");
                    }
                }
            });
        })
        .expect("failed to spawn sigwin-core thread");

    Box::into_raw(Box::new(SigwinHandle {
        worker: Some(worker),
        shutdown: Some(shutdown_tx),
        commands: Some(cmd_tx),
    }))
}

/// Queue a plain text message to a conversation thread (`contact:<uuid>` or
/// `group:<hex>`), optionally as a reply (pass `quote_timestamp <= 0` for
/// none). Returns true if the command was queued.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the strings must be
/// valid NUL-terminated UTF-8, or null where noted.
#[no_mangle]
pub unsafe extern "C" fn sigwin_send_text(
    handle: *mut SigwinHandle,
    thread_id: *const c_char,
    text: *const c_char,
    quote_timestamp: i64,
    quote_author: *const c_char,
    quote_text: *const c_char,
) -> bool {
    if handle.is_null() {
        return false;
    }
    let handle = &*handle;
    let (Some(thread_id), Some(text)) = (opt_string(thread_id), opt_string(text)) else {
        return false;
    };
    let quote_timestamp = (quote_timestamp > 0).then_some(quote_timestamp as u64);
    let quote_author = opt_string(quote_author);
    let quote_text = opt_string(quote_text);
    match &handle.commands {
        Some(tx) => tx
            .unbounded_send(Command::SendText { thread_id, text, quote_timestamp, quote_author, quote_text })
            .is_ok(),
        None => false,
    }
}

/// Queue an image/video attachment (base64-encoded bytes) to a conversation
/// thread, with an optional caption. Returns true if the command was queued.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the strings must be
/// valid NUL-terminated UTF-8, or null where noted.
#[no_mangle]
pub unsafe extern "C" fn sigwin_send_attachment(
    handle: *mut SigwinHandle,
    thread_id: *const c_char,
    caption: *const c_char,
    content_type: *const c_char,
    file_name: *const c_char,
    data_base64: *const c_char,
) -> bool {
    if handle.is_null() {
        return false;
    }
    let handle = &*handle;
    let (Some(thread_id), Some(content_type), Some(data_base64)) =
        (opt_string(thread_id), opt_string(content_type), opt_string(data_base64))
    else {
        return false;
    };
    let caption = opt_string(caption);
    let file_name = opt_string(file_name);
    match &handle.commands {
        Some(tx) => tx
            .unbounded_send(Command::SendAttachment { thread_id, caption, content_type, file_name, data_base64 })
            .is_ok(),
        None => false,
    }
}

/// Queue an emoji reaction (or its removal) on a message. `target_author` is
/// the sender's UUID as a string. Returns true if the command was queued.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the strings must be
/// valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn sigwin_send_reaction(
    handle: *mut SigwinHandle,
    thread_id: *const c_char,
    target_timestamp: i64,
    target_author: *const c_char,
    emoji: *const c_char,
    remove: bool,
) -> bool {
    if handle.is_null() || target_timestamp <= 0 {
        return false;
    }
    let handle = &*handle;
    let (Some(thread_id), Some(target_author), Some(emoji)) =
        (opt_string(thread_id), opt_string(target_author), opt_string(emoji))
    else {
        return false;
    };
    match &handle.commands {
        Some(tx) => tx
            .unbounded_send(Command::SendReaction {
                thread_id,
                target_timestamp: target_timestamp as u64,
                target_author,
                emoji,
                remove,
            })
            .is_ok(),
        None => false,
    }
}

/// Queue a "delete for everyone" for a message this device sent.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the string must be
/// valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn sigwin_delete_message(
    handle: *mut SigwinHandle,
    thread_id: *const c_char,
    target_timestamp: i64,
) -> bool {
    if handle.is_null() || target_timestamp <= 0 {
        return false;
    }
    let handle = &*handle;
    let Some(thread_id) = opt_string(thread_id) else {
        return false;
    };
    match &handle.commands {
        Some(tx) => tx
            .unbounded_send(Command::DeleteMessage { thread_id, target_timestamp: target_timestamp as u64 })
            .is_ok(),
        None => false,
    }
}

/// Queue a typing-started/stopped notification for a thread.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the string must be
/// valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn sigwin_send_typing(
    handle: *mut SigwinHandle,
    thread_id: *const c_char,
    typing: bool,
) -> bool {
    if handle.is_null() {
        return false;
    }
    let handle = &*handle;
    let Some(thread_id) = opt_string(thread_id) else {
        return false;
    };
    match &handle.commands {
        Some(tx) => tx.unbounded_send(Command::SendTyping { thread_id, typing }).is_ok(),
        None => false,
    }
}

/// Queue a read receipt directly to a message's original sender.
///
/// # Safety
/// `handle` must be a live handle from [`sigwin_start`]; the string must be
/// valid NUL-terminated UTF-8.
#[no_mangle]
pub unsafe extern "C" fn sigwin_send_read_receipt(
    handle: *mut SigwinHandle,
    target_author: *const c_char,
    timestamp: i64,
) -> bool {
    if handle.is_null() || timestamp <= 0 {
        return false;
    }
    let handle = &*handle;
    let Some(target_author) = opt_string(target_author) else {
        return false;
    };
    match &handle.commands {
        Some(tx) => tx
            .unbounded_send(Command::SendReadReceipt { target_author, timestamp: timestamp as u64 })
            .is_ok(),
        None => false,
    }
}

/// Stop the core and free the handle. Signals shutdown, then blocks until the
/// worker (runtime + store) has wound down, so the store can be deleted after.
///
/// # Safety
/// `handle` must be a pointer returned by [`sigwin_start`] and not used again.
#[no_mangle]
pub unsafe extern "C" fn sigwin_stop(handle: *mut SigwinHandle) {
    if handle.is_null() {
        return;
    }
    let mut boxed = Box::from_raw(handle);
    boxed.commands.take();
    if let Some(tx) = boxed.shutdown.take() {
        let _ = tx.send(());
    }
    if let Some(worker) = boxed.worker.take() {
        let _ = worker.join();
    }
}

fn event_to_json(event: &IncomingEvent) -> String {
    serde_json::to_string(event).unwrap_or_else(|_| "{\"type\":\"Other\",\"description\":\"serialize failed\"}".to_string())
}
