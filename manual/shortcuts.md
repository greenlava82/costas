# Keyboard shortcuts

In FT8 and FT4, press ++f1++ in Costas to see this list. In PSK31, PSK63 and RTTY ++f1++ sends your CQ macro, so open
the list from **☰ › Keyboard shortcuts** instead.

| Key | Action |
| --- | --- |
| ++enter++ | Call the selected station (does nothing during a contact) |
| ++up++, ++down++ | Move the selection in Next in line |
| ++i++ | Open or close the details strip of the selected station (clicking its call does the same) |
| ++esc++ | Halt Tx. In a contact: abandon it. Once reports are exchanged, the first press only stops transmitting and keeps listening for 4 slots (about a minute on FT8) so a late `RR73` still logs; then the contact closes by itself. Press again, or start another contact, to close it at once. A `RR73` that comes later shows under Calling you as a reply after you stopped; click it to log. With a list or menu open, Esc only closes it (and halts if you are transmitting) |
| ++ctrl+q++ | Call CQ / Stop CQ |
| ++ctrl+r++ | Resend |
| ++ctrl+l++ | Log now |
| ++ctrl+1++ to ++ctrl+5++ | Operate, Raw decodes, Reach, Log, [Awards](awards.md). The number pad digits work too |
| ++ctrl+6++ | The [Contest](contests.md) view, while a contest is picked |
| ++ctrl+tab++, ++ctrl+shift+tab++ | Next view, previous view |
| ++ctrl+m++ | [Mini mode](operate.md#mini-mode): a small strip that stays on top |
| ++f1++ | This list |

The view keys work wherever you are typing, including the PSK and RTTY typing line, because they type nothing.
Hover over a view's name at the top right to see its key.

## PSK31, PSK63 and RTTY

On the Operate screen, typing always goes to the typing line under the conversation, wherever you last clicked.

| Key | While receiving | While sending |
| --- | --- | --- |
| Typing, ++space++, ++backspace++ | Edit the typing line | Go out as you type; Backspace takes back what has not gone |
| ++enter++ | Send what you typed, or the next step's macro | A new line; on an empty line, back to receive |
| ++f1++ to ++f12++ | Send that macro | Add that macro to what is going out |
| ++esc++ | Abandon the contact | Stop sending; press again to abandon the contact |

See [PSK31, PSK63 and RTTY](keyboard-modes.md#making-a-contact).

![The shortcuts window](images/shortcuts.png){ width="460" }
