import Foundation
import NIOCore

/// A protocol representing anything that can be rendered as HTML.
public protocol HTMLContent: Sendable {
    func write(to buffer: inout ByteBuffer)
}

/// A simple implementation for raw strings.
extension String: HTMLContent {
    public func write(to buffer: inout ByteBuffer) {
        buffer.writeString(self)
    }
}

/// A container for pre-encoded HTML.
public struct RawHTML: HTMLContent {
    let content: String
    public init(_ content: String) { self.content = content }
    public func write(to buffer: inout ByteBuffer) {
        buffer.writeString(content)
    }
}

/// The result builder that allows for declarative HTML construction.
@resultBuilder
public struct HTMLBuilder {
    public static func buildBlock(_ components: HTMLContent...) -> HTMLContent {
        CombinedContent(components: components)
    }
    
    public static func buildOptional(_ component: HTMLContent?) -> HTMLContent {
        component ?? CombinedContent(components: [])
    }
    
    public static func buildEither(first component: HTMLContent) -> HTMLContent {
        component
    }
    
    public static func buildEither(second component: HTMLContent) -> HTMLContent {
        component
    }
    
    public static func buildArray(_ components: [HTMLContent]) -> HTMLContent {
        CombinedContent(components: components)
    }
    
    /// Allow for raw strings to be used directly in the builder.
    public static func buildExpression(_ expression: String) -> HTMLContent {
        expression
    }
    
    /// Allow for components to be used directly.
    public static func buildExpression(_ expression: HTMLContent) -> HTMLContent {
        expression
    }
}

/// Internal type to combine multiple HTML pieces.
struct CombinedContent: HTMLContent {
    let components: [HTMLContent]
    func write(to buffer: inout ByteBuffer) {
        for component in components {
            component.write(to: &buffer)
        }
    }
}
