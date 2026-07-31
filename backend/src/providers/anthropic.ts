import type { ChatProvider, ChatRequest, StreamEvent, ToolSchema } from "./types.js";
import { sseLines } from "./openai.js";

// Anthropic Messages API - streaming + tool use.
export class AnthropicProvider implements ChatProvider {
  readonly id = "anthropic";
  supports(model: string): boolean {
    return model.startsWith("claude-");
  }

  async *chat(req: ChatRequest): AsyncGenerator<StreamEvent> {
    const system = req.messages.find((m) => m.role === "system")?.content;
    const messages = req.messages
      .filter((m) => m.role !== "system")
      .map((m) => {
        if (m.role === "assistant" && m.toolCalls?.length) {
          const blocks: unknown[] = [];
          if (m.content) blocks.push({ type: "text", text: m.content });
          for (const tc of m.toolCalls)
            blocks.push({ type: "tool_use", id: tc.id, name: tc.name, input: tc.args });
          return { role: "assistant", content: blocks };
        }
        if (m.role === "tool") {
          return {
            role: "user",
            content: [{ type: "tool_result", tool_use_id: m.toolCallId, content: m.content }],
          };
        }
        return { role: m.role, content: m.content };
      });

    const body = {
      model: req.model,
      max_tokens: 4096,
      stream: true,
      ...(system ? { system } : {}),
      messages,
      tools: req.tools.map(toAnthropicTool),
    };

    const res = await fetch("https://api.anthropic.com/v1/messages", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-api-key": req.apiKey,
        "anthropic-version": "2023-06-01",
      },
      body: JSON.stringify(body),
    });

    if (!res.ok || !res.body) {
      yield { type: "error", message: `Anthropic ${res.status}: ${await res.text()}` };
      return;
    }

    let curTool: { id: string; name: string; args: string } | null = null;

    for await (const data of sseLines(res.body)) {
      let ev: any;
      try {
        ev = JSON.parse(data);
      } catch {
        continue;
      }
      switch (ev.type) {
        case "message_start":
          // Giriş token sayısı message_start'ta gelir — atlarsak input maliyeti 0 görünür.
          if (ev.message?.usage?.input_tokens)
            yield { type: "usage", inputTokens: ev.message.usage.input_tokens, outputTokens: 0 };
          break;
        case "content_block_start":
          if (ev.content_block?.type === "tool_use") {
            curTool = { id: ev.content_block.id, name: ev.content_block.name, args: "" };
          }
          break;
        case "content_block_delta":
          if (ev.delta?.type === "text_delta") yield { type: "token", text: ev.delta.text };
          if (ev.delta?.type === "input_json_delta" && curTool)
            curTool.args += ev.delta.partial_json;
          break;
        case "content_block_stop":
          if (curTool) {
            yield { type: "tool_call", id: curTool.id, name: curTool.name, args: safeParse(curTool.args) };
            curTool = null;
          }
          break;
        case "message_delta":
          if (ev.usage)
            yield { type: "usage", inputTokens: 0, outputTokens: ev.usage.output_tokens ?? 0 };
          break;
      }
    }
    yield { type: "done" };
  }
}

function toAnthropicTool(t: ToolSchema) {
  return { name: t.name, description: t.description, input_schema: t.parameters };
}

function safeParse(s: string): Record<string, unknown> {
  try {
    return JSON.parse(s || "{}");
  } catch {
    return {};
  }
}
