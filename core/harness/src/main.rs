//! Sigwin CLI harness — Phase 1.
//!
//! Headless proof that the hardest part works: it opens the encrypted store,
//! links to your phone by rendering a QR code in the terminal, then streams
//! incoming messages (text + attachment metadata) to stdout. Test this manually
//! against a real phone before any UI is built.
//!
//! Config via env vars:
//!   SIGWIN_DB          path to the sqlite store   (default: sigwin.db)
//!   SIGWIN_PASSPHRASE  sqlcipher passphrase       (default: unencrypted — dev only)
//!   SIGWIN_DEVICE      linked-device display name (default: Sigwin)
//!   RUST_LOG           tracing filter             (default: info)

use sigwin_core::signal::{self, AttachmentInfo, IncomingEvent};

// libsignal/presage use deep stacks (crypto + protobuf state machines); the
// default ~1 MB main-thread stack on Windows overflows. Run everything on a
// thread with a generous stack, and give the tokio worker threads the same.
const STACK_SIZE: usize = 16 * 1024 * 1024;

fn main() -> anyhow::Result<()> {
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "info".into()),
        )
        .init();

    std::thread::Builder::new()
        .name("sigwin-main".into())
        .stack_size(STACK_SIZE)
        .spawn(run)?
        .join()
        .expect("sigwin-main thread panicked")
}

fn run() -> anyhow::Result<()> {
    let rt = tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .thread_stack_size(STACK_SIZE)
        .build()?;
    rt.block_on(async_main())
}

async fn async_main() -> anyhow::Result<()> {
    let db_path = std::env::var("SIGWIN_DB").unwrap_or_else(|_| "sigwin.db".to_string());
    let passphrase = std::env::var("SIGWIN_PASSPHRASE").ok();
    let device_name = std::env::var("SIGWIN_DEVICE").unwrap_or_else(|_| "Sigwin".to_string());

    println!("Sigwin harness — Phase 1 (link + receive)");
    println!(
        "store: {db_path}   encrypted: {}   device: {device_name}\n",
        passphrase.is_some()
    );

    let (_commands_tx, commands_rx) = futures::channel::mpsc::unbounded();

    signal::run(
        &db_path,
        passphrase.as_deref(),
        &device_name,
        |url| {
            println!("\nOn your phone: Signal → Settings → Linked devices → Link new device");
            println!("Scan this QR code:\n");
            if let Err(e) = qr2term::print_qr(&url) {
                eprintln!("(couldn't render QR: {e}) raw provisioning URL:\n{url}");
            }
            println!();
        },
        print_event,
        commands_rx,
    )
    .await
}

fn print_event(event: IncomingEvent) {
    match event {
        IncomingEvent::Conversation(c) => println!(
            "[conversation] {}  ({})  last: {:?}",
            c.title, c.thread_id, c.last_body
        ),
        IncomingEvent::ConversationAvatar { thread_id, .. } => {
            println!("[avatar] {thread_id}")
        }
        IncomingEvent::QueueEmpty => println!("[caught up — now live]"),
        IncomingEvent::ContactsSynced => println!("[contacts synced from primary device]"),
        IncomingEvent::Other { description } => println!("[other: {description}]"),
        IncomingEvent::Message(m) => {
            println!("── message ───────────────────────────────");
            println!("  from:      {}", m.sender);
            println!("  timestamp: {}", m.timestamp);
            if m.group {
                println!("  group:     yes");
            }
            if let Some(q) = &m.quote {
                println!("  reply-to:  [{}] {:?}", q.author, q.text);
            }
            if let Some(body) = &m.body {
                println!("  text:      {body}");
            }
            if let Some(p) = &m.preview {
                println!("  preview:   {} — {:?}", p.url, p.title);
            }
            for a in &m.attachments {
                print_attachment(a);
            }
        }
        IncomingEvent::MessageEdited { thread_id, target_timestamp, new_body, .. } => {
            println!("[edit] {thread_id} @ {target_timestamp} -> {new_body:?}")
        }
        IncomingEvent::MessageDeleted { thread_id, target_timestamp } => {
            println!("[delete] {thread_id} @ {target_timestamp}")
        }
        IncomingEvent::Reaction { thread_id, target_timestamp, emoji, remove, .. } => {
            println!("[reaction] {thread_id} @ {target_timestamp} {emoji} remove={remove}")
        }
        IncomingEvent::Typing { thread_id, is_typing, .. } => {
            println!("[typing] {thread_id} is_typing={is_typing}")
        }
        IncomingEvent::Receipt { sender, receipt_type, timestamps } => {
            println!("[receipt] from={sender} type={receipt_type} timestamps={timestamps:?}")
        }
    }
}

fn print_attachment(a: &AttachmentInfo) {
    let kind = a.content_type.as_deref().unwrap_or("?");
    let name = a.file_name.as_deref().unwrap_or("(unnamed)");
    match a.size {
        Some(size) => println!("  attachment: {kind}  {size} bytes  {name}"),
        None => println!("  attachment: {kind}  {name}"),
    }
}
