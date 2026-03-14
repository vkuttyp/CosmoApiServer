import Foundation
import CosmoApiServer

// MARK: - Benchmark Components

struct BenchItem: Sendable {
    let id: Int
    let name: String
    let value: Double
    let date: String
}

struct BenchComponent: Component {
    let items: [BenchItem]
    
    var body: HTMLContent {
        Table {
            Thead {
                Tr {
                    Th { "ID" }
                    Th { "Name" }
                    Th { "Value" }
                    Th { "Date" }
                }
            }
            Tbody {
                for item in items {
                    Tr {
                        Td { "\(item.id)" }
                        Td { item.name }
                        Td { "\(item.value)" }
                        Td { item.date }
                    }
                }
            }
        }
    }
}

// MARK: - Benchmark Server

func makeApp(port: Int, http2: Bool) -> CosmoWebApplication {
    let builder = CosmoWebApplicationBuilder()
    builder.listenOn(port: port)
    builder.useErrorHandling()
    if http2 { builder.useHttp2() }
    builder.useThreads(ProcessInfo.processInfo.activeProcessorCount)
    return builder.build()
}

let isoFormatter: ISO8601DateFormatter = {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    return f
}()

struct StatusResponse: Encodable {
    let status: String; let timestamp: String; let server: String
}

let benchItems: [BenchItem] = (1...100).map { i in
    BenchItem(id: i, name: "Item \(i)", value: Double(i) * 1.23, date: isoFormatter.string(from: Date().addingTimeInterval(TimeInterval(i * 86400))))
}

func registerRoutes(_ app: CosmoWebApplication, label: String) {
    app.get("/ping") { ctx in
        ctx.response.writeText("pong")
    }
    app.get("/json") { ctx in
        try ctx.response.writeJson(StatusResponse(
            status: "ok",
            timestamp: isoFormatter.string(from: Date()),
            server: label
        ))
    }
    app.post("/echo") { ctx in
        ctx.response.body = ctx.request.body
        ctx.response.headers["Content-Type"] = ctx.request.header("content-type") ?? "application/octet-stream"
    }
    app.get("/route/{id}") { ctx in
        let id = ctx.request.routeValues["id"] ?? "unknown"
        try ctx.response.writeJson(["id": id])
    }
    app.get("/middleware") { ctx in
        try ctx.response.writeJson(["path": ctx.request.path, "method": ctx.request.method.rawValue])
    }
    app.get("/bench") { ctx in
        ctx.response.writeHTML(BenchComponent(items: benchItems))
    }
}

let h1App  = makeApp(port: 19000, http2: false)
let h2cApp = makeApp(port: 19002, http2: true)

registerRoutes(h1App,  label: "CosmoApiServer-Swift/h1")
registerRoutes(h2cApp, label: "CosmoApiServer-Swift/h2c")

print("=== CosmoApiServer-Swift Benchmark ===")
print("HTTP/1.1  → http://127.0.0.1:19000")
print("h2c       → http://127.0.0.1:19002")
print("Threads: \(ProcessInfo.processInfo.activeProcessorCount)")

try await withThrowingTaskGroup(of: Void.self) { group in
    group.addTask { try await h1App.run() }
    group.addTask { try await h2cApp.run() }
    try await group.next()
}
