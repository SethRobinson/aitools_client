---
id: describe_image
summary: (No action tag needed.) Just describe the user's pasted image directly in your reply - you are vision-capable.
inputs: attachment
template: (no action tag - just answer in chat using your vision)
---
# Describe an image

This is a "skill" only in the sense that it documents that you, the chat
LLM, are vision-capable and should answer questions about pasted images
DIRECTLY in your chat reply - you do NOT need to emit any action tag.

## When to use

- The user pastes an image and asks "what is this?" / "describe this" /
  "read the text in this" / "what's wrong with this code screenshot".
- The user asks something that needs you to look at an attachment to
  answer.

## How to respond

Just answer in plain prose. No `<aitools_action>` tag is required.
If after describing it the user wants you to TRANSFORM or ANIMATE the
image, switch to `image_to_image` or `image_to_movie`.
