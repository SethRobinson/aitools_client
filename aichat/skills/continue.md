---
id: continue
summary: Control action - request ONE more turn for yourself when you are not finished (you described work you still need to do, or want to verify/comment after an image you just spawned). No image, no GPU. Do NOT use it after read_skill or inspect_image resume="true" - those already auto-continue. The host caps consecutive self-continues.
template: <aitools_action skill="continue"/>
---
# Continue (request another turn)

You decide when you need another turn. Emit `<aitools_action skill="continue"/>`
when your reply is not the end of the work and you want the host to send you one
automatic `(continue)` turn so you can keep going - without the user clicking
Send.

## When to use

- You said you will generate / edit / fix / animate / compose something but, for
  whatever reason, you did not emit its action tag in THIS reply and want to do it
  on the next turn. (Better: just emit the real action tag now. Use `continue`
  only when you genuinely need a fresh turn.)
- You just spawned an image/movie and want a follow-up turn to look at it,
  comment, or run a verification step.
- You finished one step of a multi-step plan and the next step needs a new turn.

## When NOT to use

- After `read_skill` - it ALREADY auto-continues. Adding `continue` double-fires.
- After `inspect_image resume="true"` - it ALREADY auto-continues with the result.
- When you have nothing left to do. Just end your turn normally.

## Rules

- It takes no attributes: `<aitools_action skill="continue"/>`.
- It is a control action - it leaves no visible bubble and spawns no image.
- You can emit it alongside real action tags in the same reply (e.g. emit an
  `image_to_image` edit AND a `continue` so you get a turn to verify it).
- The host caps how many times in a row you may self-continue; once the cap is
  hit it stops and waits for the user, so don't rely on it to loop forever.
