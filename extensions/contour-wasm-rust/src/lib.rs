use std::cell::RefCell;

wit_bindgen::generate!({ path: "wit", world: "statusline" });
use terminal_extensions::statusline::host;

const MAX_FRAME_BYTES: usize = 64 * 1024;

struct Frame {
    variable: String,
    bytes: Vec<u8>,
}

impl Frame {
    fn replace(&mut self, name: &str, value: &str) -> bool {
        if name != self.variable || value.len() > MAX_FRAME_BYTES || self.bytes == value.as_bytes() {
            return false;
        }
        self.bytes.clear();
        self.bytes.extend_from_slice(value.as_bytes());
        true
    }
}

thread_local! {
    static FRAME: RefCell<Frame> = RefCell::new(Frame {
        variable: "statusline_frame".into(),
        bytes: Vec::new(),
    });
}

struct StatusLine;
impl Guest for StatusLine {
    fn load(config: Vec<(String, String)>) -> EventKinds {
        FRAME.with(|frame| {
            let mut frame = frame.borrow_mut();
            if let Some((_, variable)) = config.into_iter().find(|(key, _)| key == "variable") {
                frame.variable = variable;
            }
            for (name, value) in host::state().user_vars {
                frame.replace(&name, &value);
            }
        });
        EventKinds::USER_VAR
    }

    fn update(event: Event) -> RenderRequest {
        let changed = match event {
            Event::UserVar((name, value)) => FRAME.with(|frame| frame.borrow_mut().replace(&name, &value)),
            Event::Resized(_) => true,
            _ => false,
        };
        if changed { RenderRequest::Render } else { RenderRequest::None }
    }

    fn render(_cols: u32, _rows: u32) -> Vec<u8> {
        FRAME.with(|frame| frame.borrow().bytes.clone())
    }
}
export!(StatusLine);

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn preserves_frame_bytes_and_rejects_unrelated_or_oversized_updates() {
        let mut frame = Frame { variable: "test_frame".into(), bytes: Vec::new() };
        let value = "\x1b[38;2;4;0;253m日 🛰 café\x1b[0m";
        assert!(frame.replace("test_frame", value));
        assert_eq!(frame.bytes, value.as_bytes());
        assert!(!frame.replace("test_frame", value));
        assert!(!frame.replace("other", "wrong"));
        assert!(!frame.replace("test_frame", &"x".repeat(MAX_FRAME_BYTES + 1)));
        assert_eq!(frame.bytes, value.as_bytes());
        assert!(frame.replace("test_frame", ""));
        assert!(frame.bytes.is_empty());
    }
}
