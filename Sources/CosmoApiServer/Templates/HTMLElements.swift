import Foundation
import NIOCore

/// A protocol for reusable UI components.
public protocol Component: HTMLContent {
    @HTMLBuilder var body: HTMLContent { get }
}

extension Component {
    public func write(to buffer: inout ByteBuffer) {
        body.write(to: &buffer)
    }
}

/// A base class for standard HTML tags.
public struct HTMLElement: HTMLContent {
    let tag: String
    let attributes: [String: String]
    let content: HTMLContent?
    
    public init(tag: String, attributes: [String: String] = [:], @HTMLBuilder content: () -> HTMLContent? = { nil }) {
        self.tag = tag
        self.attributes = attributes
        self.content = content()
    }
    
    public func write(to buffer: inout ByteBuffer) {
        buffer.writeString("<\(tag)")
        for (name, value) in attributes {
            buffer.writeString(" \(name)=\"\(value)\"")
        }
        
        if let content = content {
            buffer.writeString(">")
            content.write(to: &buffer)
            buffer.writeString("</\(tag)>")
        } else {
            buffer.writeString(" />")
        }
    }
}

// MARK: - Convenience Elements

public func Div(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "div", attributes: attributes, content: content)
}

public func P(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "p", attributes: attributes, content: content)
}

public func H1(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "h1", attributes: attributes, content: content)
}

public func Table(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "table", attributes: attributes, content: content)
}

public func Tr(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "tr", attributes: attributes, content: content)
}

public func Td(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "td", attributes: attributes, content: content)
}

public func Th(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "th", attributes: attributes, content: content)
}

public func Thead(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "thead", attributes: attributes, content: content)
}

public func Tbody(attributes: [String: String] = [:], @HTMLBuilder content: @escaping () -> HTMLContent) -> HTMLElement {
    HTMLElement(tag: "tbody", attributes: attributes, content: content)
}
