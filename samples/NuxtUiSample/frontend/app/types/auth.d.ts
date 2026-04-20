declare module '#auth-utils' {
  interface User {
    username: string
    role: string
  }

  interface SecureSessionData {
    token?: string
  }
}

export {}
