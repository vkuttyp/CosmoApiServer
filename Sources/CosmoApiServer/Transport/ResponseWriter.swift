import Foundation
import NIOCore
import NIOHTTP1

/// Writes a completed HttpResponse back as NIOHTTP1 response parts.
enum ResponseWriter {
    static func write(response: HttpResponse, context: ChannelHandlerContext, keepAlive: Bool) {
        // Build HTTPHeaders in one allocation from all pairs
        var pairs = response.headers.map { ($0.key, $0.value) }
        if response.headers["Content-Length"] == nil {
            pairs.append(("Content-Length", String(response.body.readableBytes)))
        }
        pairs.append(("Connection", keepAlive ? "keep-alive" : "close"))
        let headers = HTTPHeaders(pairs)

        let status = HTTPResponseStatus(statusCode: response.statusCode,
                                        reasonPhrase: response.reasonPhrase)
        let head = HTTPResponseHead(version: .http1_1, status: status, headers: headers)

        context.write(NIOAny(HTTPServerResponsePart.head(head)), promise: nil)

        if response.body.readableBytes > 0 {
            context.write(NIOAny(HTTPServerResponsePart.body(.byteBuffer(response.body))), promise: nil)
        }

        if keepAlive {
            context.writeAndFlush(NIOAny(HTTPServerResponsePart.end(nil)), promise: nil)
        } else {
            context.writeAndFlush(NIOAny(HTTPServerResponsePart.end(nil))).whenComplete { _ in
                context.close(promise: nil)
            }
        }
    }
}
