# Texts

In *Texts* you keep short abbreviations that K-Desktop swaps for a longer text when you type them in any program: your
signature, your address, an answer you keep repeating.

1. *Texts → New text*.
2. Type the **abbreviation** (`sig`) and the **text** that replaces it.
3. In any program, type the prefix and the abbreviation (`;sig`): it is erased and the text appears.

Try each one in the **Try it** box on the same page. Inside K-Desktop they are only written there, never while you edit
them.

## Options

- **Write as you type**: turns every text on or off at once. Each text also has its own **On** switch.
- **Prefix**: goes before every abbreviation (`;` by default) so normal writing never sets one off. You can change it or
  leave it empty; without a prefix, an abbreviation only goes off as a whole word (`ok` does not go off inside `book`).

## When it goes off

- As soon as you finish typing the abbreviation.
- If another abbreviation starts the same (`;fe` and `;fecha`), the short one waits for **Space**, **Tab** or **Enter**.
  With Space, the space stays after the text. Under the abbreviation you can see how yours goes off.
- Typing the abbreviation in capitals (`;SIG`) writes the text in capitals; with the first letter in capitals (`;Sig`),
  the text starts with a capital.
- **It does not go off** in password boxes, in full-screen games or films, in the programs of *Disable in*, or while
  K-Desktop is paused.

## What the text can hold

Type `{` in the text to pick:

| Variable | What it puts |
|---|---|
| `{cursor}` | Nothing: leaves the cursor there at the end (for example `<b>{cursor}</b>`) |
| `{date}` | Today's date (2026-10-29) |
| `{time}` | The time (09:30:05) |
| `{clipboard}` | The copied text |

Short texts are typed. Long ones, or with several lines, are pasted (so a line break does not send the message in a
chat) and what you had copied goes back where it was.
