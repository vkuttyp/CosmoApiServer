import Foundation
import NIOCore

public final class HttpResponse: @unchecked Sendable {
    public var statusCode: Int = 200
    public var reasonPhrase: String = "OK"
    public var headers: [String: String] = Dictionary(minimumCapacity: 8)
    public var body: ByteBuffer = ByteBuffer()

    public init() {}

    public func write(_ buffer: ByteBuffer) {
        self.body = buffer
        headers["Content-Length"] = String(buffer.readableBytes)
    }

    public func writeText(_ text: String, contentType: String = "text/plain; charset=utf-8") {
        var buffer = ByteBuffer()
        buffer.writeString(text)
        headers["Content-Type"] = contentType
        write(buffer)
    }

    public func writeHTML(_ content: HTMLContent) {
        var buffer = ByteBuffer()
        content.write(to: &buffer)
        headers["Content-Type"] = "text/html; charset=utf-8"
        write(buffer)
    }

    public func writeJson<T: Encodable>(_ value: T) throws {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(value)
        var buffer = ByteBuffer()
        buffer.writeData(data)
        headers["Content-Type"] = "application/json; charset=utf-8"
        write(buffer)
    }

    public func setStatus(_ code: Int) {
        statusCode = code
        reasonPhrase = Self.reasonPhrase(for: code)
    }

    static func reasonPhrase(for code: Int) -> String {
        switch code {
        case 200: return "OK"
        case 201: return "Created"
        case 204: return "No Content"
        case 206: return "Partial Content"
        case 301: return "Moved Permanently"
        case 302: return "Found"
        case 304: return "Not Modified"
        case 400: return "Bad Request"
        case 401: return "Unauthorized"
        case 403: return "Forbidden"
        case 404: return "Not Found"
        case 405: return "Method Not Allowed"
        case 409: return "Conflict"
        case 500: return "Internal Server Error"
        case 501: return "Not Implemented"
        case 503: return "Service Unavailable"
        default:  return "Unknown"
        }
    }
}
