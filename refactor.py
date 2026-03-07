import os, re

def write_to_path(path, content):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, 'wb') as f: f.write(content.encode('utf-8'))

base = '/Users/kutty/dev/CosmoSQLClient/CosmoSQLClient-Swift'

# 1. SQLDatabase.swift
write_to_path(base + '/Sources/CosmoSQLCore/SQLDatabase.swift', r'''import Foundation

public protocol SQLDatabase: Sendable {
    func query(_ sql: String, _ binds: [SQLValue]) async throws -> [SQLRow]
    func execute(_ sql: String, _ binds: [SQLValue]) async throws -> Int
    func close() async throws
    var advanced: any AdvancedSQLDatabase { get }
}

public protocol AdvancedSQLDatabase: Sendable {
    func queryStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<SQLRow, any Error>
    func queryJsonStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<Data, any Error>
}

public extension SQLDatabase {
    func query(_ sql: String) async throws -> [SQLRow] {
        try await query(sql, [])
    }

    @discardableResult
    func execute(_ sql: String) async throws -> Int {
        try await execute(sql, [])
    }

    func query<T: Decodable>(_ sql: String, _ binds: [SQLValue] = [], as type: T.Type = T.self) async throws -> [T] {
        let rows = try await query(sql, binds)
        return try rows.map { row in try SQLRowDecoder().decode(T.self, from: row) }
    }
}

public extension AdvancedSQLDatabase {
    func queryJsonStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<Data, any Error> {
        AsyncThrowingStream { cont in
            cont.finish(throwing: SQLError.unsupported("JSON streaming not supported by this driver"))
        }
    }
}
''')

# 2. Connection Refactoring
def refactor_conn(path, name):
    with open(path, 'r') as f: content = f.read()
    content = content.replace(f'public final class {name}: SQLDatabase,', f'public final class {name}: SQLDatabase, AdvancedSQLDatabase,')
    content = content.replace('func queryStream(_ sql: String, _ binds: [SQLValue] = []) -> AsyncThrowingStream<SQLRow, Error>',
                              'func queryStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<SQLRow, any Error>')
    content = content.replace('func queryJsonStream(_ sql: String, _ binds: [SQLValue] = []) -> AsyncThrowingStream<Data, Error>',
                              'func queryJsonStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<Data, any Error>')
    if 'public var advanced: any AdvancedSQLDatabase' not in content:
        idx = content.find(f'public final class {name}')
        brace_idx = content.find('{', idx)
        content = content[:brace_idx+1] + '\n    public var advanced: any AdvancedSQLDatabase { self }\n' + content[brace_idx+1:]
    write_to_path(path, content)

refactor_conn(base + '/Sources/CosmoPostgres/PostgresConnection.swift', 'PostgresConnection')
refactor_conn(base + '/Sources/CosmoMSSQL/MSSQLConnection.swift', 'MSSQLConnection')
refactor_conn(base + '/Sources/CosmoMySQL/MySQLConnection.swift', 'MySQLConnection')

# SQLite Connection
with open(base + '/Sources/CosmoSQLite/SQLiteConnection.swift', 'r') as f: content = f.read()
content = content.replace('public final class SQLiteConnection: SQLDatabase,', 'public final class SQLiteConnection: SQLDatabase, AdvancedSQLDatabase,')
if 'public var advanced: any AdvancedSQLDatabase' not in content:
    idx = content.find('public final class SQLiteConnection')
    brace_idx = content.find('{', idx)
    content = content[:brace_idx+1] + r'''
    public var advanced: any AdvancedSQLDatabase { self }
    public func queryStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<SQLRow, any Error> {
        AsyncThrowingStream { cont in Task { do { let r = try await self.query(sql, binds); for row in r { cont.yield(row) }; cont.finish() } catch { cont.finish(throwing: error) } } }
    }
    public func queryJsonStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<Data, any Error> {
        AsyncThrowingStream { cont in Task { do { for try await r in self.queryStream(sql, binds) { if let t = r.values.first?.asString() { cont.yield(Data(t.utf8)) } }; cont.finish() } catch { cont.finish(throwing: error) } } }
    }
''' + content[brace_idx+1:]
write_to_path(base + '/Sources/CosmoSQLite/SQLiteConnection.swift', content)

# 3. Pool Refactoring Helper
def refactor_pool(path, name):
    with open(path, 'r') as f: content = f.read()
    
    # Clean up previous messy attempts
    content = re.sub(r'struct .*?Advanced: AdvancedSQLDatabase \{.*?\}', '', content, flags=re.DOTALL)
    content = content.replace(': SQLDatabase, AdvancedSQLDatabase', '')
    content = content.replace('public actor ' + name + ' {', 'public actor ' + name + ': SQLDatabase {')
    content = re.sub(r'public nonisolated var advanced: any AdvancedSQLDatabase \{.*?\}', '', content, flags=re.DOTALL)
    
    # 1. Add Conformance and Property
    content = content.replace(f'public actor {name}: SQLDatabase {{', 
                              f'public actor {name}: SQLDatabase {{ \n    public nonisolated var advanced: any AdvancedSQLDatabase {{ {name}Advanced(pool: self) }}\n')
    
    # 2. Add close() if missing
    if 'public func close()' not in content:
        if 'public func closeAll()' in content:
            idx = content.find('public func closeAll()')
            content = content[:idx] + 'public func close() async throws { await closeAll() }\n\n    ' + content[idx:]

    # 3. Fix internal query/execute to use connection explicitly
    # Some pools had query/execute but they were using shorthand or were missing
    # Let's just ensure they exist and are correct
    if 'public func query(_ sql: String, _ binds: [SQLValue]) async throws -> [SQLRow]' not in content:
        idx = content.find('public func acquire()')
        if idx != -1:
            methods = r'''
    public func query(_ sql: String, _ binds: [SQLValue]) async throws -> [SQLRow] {
        try await withConnection { conn in try await conn.query(sql, binds) }
    }
    public func execute(_ sql: String, _ binds: [SQLValue]) async throws -> Int {
        try await withConnection { conn in try await conn.execute(sql, binds) }
    }
'''
            content = content[:idx] + methods + content[idx:]

    # 4. Correct acquire/release calls in withConnection
    content = content.replace('release(c)', 'await release(c)')
    
    # 5. Fix any shorthand $0 in the whole file
    content = content.replace('where: { !$0.isOpen }', 'where: { conn in !conn.isOpen }')
    content = content.replace('{ $0.finish', '{ cont in cont.finish')
    
    # 6. Helper struct at the end
    helper = r'''
struct %CLASS%Advanced: AdvancedSQLDatabase {
    let pool: %CLASS%
    func queryStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<SQLRow, any Error> {
        AsyncThrowingStream { cont in
            Task {
                do {
                    let conn = try await pool.acquire()
                    do {
                        for try await row in conn.advanced.queryStream(sql, binds) {
                            cont.yield(row)
                        }
                        await pool.release(conn)
                        cont.finish()
                    } catch {
                        await pool.release(conn)
                        cont.finish(throwing: error)
                    }
                } catch {
                    cont.finish(throwing: error)
                }
            }
        }
    }
    func queryJsonStream(_ sql: String, _ binds: [SQLValue]) -> AsyncThrowingStream<Data, any Error> {
        AsyncThrowingStream { cont in
            Task {
                do {
                    let conn = try await pool.acquire()
                    do {
                        for try await data in conn.advanced.queryJsonStream(sql, binds) {
                            cont.yield(data)
                        }
                        await pool.release(conn)
                        cont.finish()
                    } catch {
                        await pool.release(conn)
                        cont.finish(throwing: error)
                    }
                } catch {
                    cont.finish(throwing: error)
                }
            }
        }
    }
}
'''.replace('%CLASS%', name)
    
    last_brace = content.rfind('}')
    content = content[:last_brace] + helper + content[last_brace:]
    write_to_path(path, content)

refactor_pool(base + '/Sources/CosmoPostgres/PostgresConnectionPool.swift', 'PostgresConnectionPool')
refactor_pool(base + '/Sources/CosmoMSSQL/MSSQLConnectionPool.swift', 'MSSQLConnectionPool')
refactor_pool(base + '/Sources/CosmoMySQL/MySQLConnectionPool.swift', 'MySQLConnectionPool')
refactor_pool(base + '/Sources/CosmoSQLite/SQLiteConnectionPool.swift', 'SQLiteConnectionPool')
