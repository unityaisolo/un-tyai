import type { ChatProvider, ChatRequest, StreamEvent } from "./types.js";

// Anahtar olmadan çok adımlı agent döngüsünü test etmek için sahte sağlayıcı.
// - "kule"/"tower" istenirse üst üste 3 küp oluşturur (her adımda bir tool_call, sonra devam).
// - Tek nesne istenirse tek tool_call üretir.
// - Elinde tool sonucu varsa (döngü devam ediyorsa) bir sonraki adımı ya da kapanışı üretir.
export class MockProvider implements ChatProvider {
  readonly id = "mock";
  supports(model: string): boolean {
    return model.startsWith("mock");
  }

  async *chat(req: ChatRequest): AsyncGenerator<StreamEvent> {
    const userMsgs = req.messages.filter((m) => m.role === "user");
    const firstUser = userMsgs[0]?.content ?? "";
    const toolResults = req.messages.filter((m) => m.role === "tool").length;
    const isTower = /kule|tower/i.test(firstUser);
    const canCreate = req.tools.some((t) => t.name === "CreateGameObject");

    if (isTower && canCreate && toolResults < 3) {
      const step = toolResults + 1;
      for (const w of `Kule için ${step}. küpü ekliyorum...`.split(" ")) {
        yield { type: "token", text: w + " " };
        await sleep(20);
      }
      yield {
        type: "tool_call",
        id: "call_" + Date.now(),
        name: "CreateGameObject",
        args: { name: `TowerBlock_${step}`, primitive: "Cube", position: [0, toolResults, 0] },
      };
      yield { type: "usage", inputTokens: 30, outputTokens: 8 };
      yield { type: "done" };
      return;
    }

    if (toolResults > 0) {
      for (const w of "Tamamlandı. İstediğin işlem sahnede uygulandı.".split(" ")) {
        yield { type: "token", text: w + " " };
        await sleep(20);
      }
      yield { type: "usage", inputTokens: 20, outputTokens: 10 };
      yield { type: "done" };
      return;
    }

    const wantsObject = /cube|küp|nesne|object|gameobject|sphere|küre/i.test(firstUser);
    for (const w of "Anladım, işlemi uyguluyorum...".split(" ")) {
      yield { type: "token", text: w + " " };
      await sleep(20);
    }
    if (wantsObject && canCreate) {
      const prim = /sphere|küre/i.test(firstUser) ? "Sphere" : "Cube";
      yield {
        type: "tool_call",
        id: "call_" + Date.now(),
        name: "CreateGameObject",
        args: { name: "NewObject", primitive: prim, position: [0, 1, 0] },
      };
    }
    yield { type: "usage", inputTokens: 42, outputTokens: 12 };
    yield { type: "done" };
  }
}

function sleep(ms: number) {
  return new Promise((r) => setTimeout(r, ms));
}
