---
id: summarize_with_small_llm
summary: Delegate a long-text summary or boilerplate task to a smaller, faster LLM instance.
inputs: none
template: <aitools_action skill="summarize_with_small_llm" prompt="self-contained instruction for the small LLM (it sees no other context)"/>
---
# Summarize with a small LLM

Use this skill when you would otherwise spend a lot of tokens on a task
that a smaller, faster LLM could handle adequately - typically:

- Summarizing a long block of text the user pasted.
- Reformatting / cleaning up a list.
- Translating a short paragraph.

The host will dispatch the request to a smaller LLM instance (one that
accepts small text jobs - e.g. role `Small` or `Any`, with or without a
`+Vision` suffix) and inject the result back into the conversation as a
system message you can read on your next turn.

## Invocation

```
<aitools_action skill="summarize_with_small_llm" prompt="The text or instruction for the small LLM to handle. Be self-contained - it has no other context."/>
```

The host scheduler picks the least busy small-job-capable instance for you.

## Rules

- Don't use this for anything requiring vision, code reasoning, or
  creativity - those should stay with you.
- The small LLM CANNOT see the conversation history. The `prompt` you send
  must be entirely self-contained.
- One delegation per turn. Wait for the result before invoking another.
