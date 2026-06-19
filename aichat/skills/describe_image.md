---
id: describe_image
summary: (No action tag needed.) For a freshly pasted image that is included in the current LLM request, describe it directly in your reply. For prior/generated chat images, use inspect_image to run a real vision sidecar.
inputs: attachment
template: (no action tag - just answer in chat using your vision)
---
# Describe an image

This is a "skill" only in the sense that it documents that you, the chat
LLM, are vision-capable and should answer questions about freshly pasted
images DIRECTLY in your chat reply when image data is present. You do NOT
need to emit any action tag for that case.

For generated images or older chat-image bubbles, do not assume the
stored prompt is accurate. Use `inspect_image` to run a real vision
sidecar on the actual pixels.

## When to use

- The user pastes an image and asks "what is this?" / "describe this" /
  "read the text in this" / "what's wrong with this code screenshot".
- The user asks something that needs you to look at an attachment to
  answer.
- The user asks about a generated/prior chat image: use `inspect_image`
  with `chat_image="N"` instead.

## How to respond

Just answer in plain prose. No `<aitools_action>` tag is required.
If after describing it the user wants you to TRANSFORM or ANIMATE the
image, switch to `image_to_image` or `image_to_movie`.
