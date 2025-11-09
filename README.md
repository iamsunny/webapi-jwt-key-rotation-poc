# 🛡️ JWT Key Rotation POC

A **.NET 8 Web API Proof-of-Concept** demonstrating **JWT-based secure link generation** with **live RSA key rotation** — ensuring zero downtime and seamless token validation during key transitions.

---

## 🚀 Features

| Feature | Description |
|----------|-------------|
| 🔑 **JWT-based secure links** | Generate short-lived secure download URLs signed with JWTs |
| 🔄 **Key rotation** | Rotate RSA signing keys without invalidating existing tokens |
| 🧩 **RSA 2048-bit** | Uses industry-standard RSA asymmetric key pairs |
| ⚙️ **Key management APIs** | Endpoints for rotation, listing, and retiring keys |
| 🌐 **JWKS endpoint** | Exposes public keys for distributed validation |
| ✅ **Graceful validation** | Tokens remain valid post-rotation until their signing key is retired |

---

## 🏗️ Architecture Overview

### Core Components

| Component | Responsibility |
|------------|----------------|
| `IKeyStore` | Defines how signing keys are managed |
| `InMemoryKeyStore` | Default key store (generates key at startup) |
| `JwtService` | Creates JWTs with the active key |
| `TokenValidationService` | Builds validation parameters; resolves key by `kid` |
| `AdminController` | Key rotation and retirement APIs |
| `JwksController` | Public JWKS endpoint |
| `LinkController` | Generates secure links |
| `DownloadController` | Validates tokens and simulates file download |

---

## 🔁 Key Rotation Lifecycle

| Step | Event | Behavior |
|------|--------|-----------|
| 1️⃣ | Create token | JWT is signed with current active key (`kid=key1`) |
| 2️⃣ | Rotate key | New key (`key2`) becomes active, `key1` marked inactive |
| 3️⃣ | Validate tokens | Both `key1` and `key2` tokens validate successfully |
| 4️⃣ | Retire old key | Tokens signed with retired key fail validation |

✅ **Graceful rotation** — Old tokens remain valid until their signing key is explicitly retired.

---

## 📡 API Endpoints

| Operation | Method | Endpoint | Description |
|------------|---------|-----------|--------------|
| 🔗 Generate secure link | `POST` | `/api/link/secure` | Creates a short-lived JWT link |
| 📥 Validate and download | `GET` | `/api/download?token={jwt}` | Validates JWT & returns download |
| 🔄 Rotate key | `POST` | `/api/admin/rotate-key` | Generates new active key |
| 🗑️ Retire key | `POST` | `/api/admin/retire/{kid}` | Deletes a key permanently |
| 📋 List keys | `GET` | `/api/admin/keys` | Lists all keys (active/inactive) |
| 🌐 JWKS | `GET` | `/.well-known/jwks.json` | Returns public key metadata |

---

## ⚙️ Configuration

**`appsettings.json`**
```json
{
  "Jwt": {
    "Issuer": "JwtKeyRotationPoc",
    "Audience": "JwtKeyRotationPoc"
  }
}
```

---

## ▶️ Run Locally

```bash
cd JwtKeyRotationPoc
dotnet run
```

🌍 API URLs  
- HTTP: `http://localhost:5166`  
- HTTPS: `https://localhost:7231`

---

## ☁️ Azure Key Vault + App Configuration Integration

Integrates with **Azure Key Vault** and **Azure App Configuration** for secure, distributed key management.

### ✅ Benefits

| Benefit | Description |
|----------|-------------|
| 🔒 Security | Keys stored in Azure Key Vault |
| 🌍 Multi-instance | All nodes share the same key |
| 🧠 Cache | Minimizes round-trips |
| 📈 Auditable | All key ops logged in Azure |
| ⚙️ Config-driven | No code changes needed |

---

## 🧩 Scaling Across Multiple Instances

| Issue | With InMemoryKeyStore | Solution |
|--------|----------------------|-----------|
| Key visibility | Local only | Use Redis-based distributed key store |
| Rotation sync | Manual | Distributed lock + cache invalidation |
| Validation | Instance-bound | Shared Redis cache |

---

## 🧠 Best Practices

| Area | Recommendation |
|-------|----------------|
| 🏗️ Single instance | Use `InMemoryKeyStore` |
| 🌐 Multi-instance | Use `DistributedKeyStore` (Redis) |
| 🔁 Key retention | Keep old keys for `maxTokenLifetime + margin` |
| ⚙️ Automation | Rotate via CI/CD or cron |
| 🔐 Security | Store private keys in Key Vault |
| 📊 Monitoring | Track token validation failures post-rotation |

---

## 🧰 Tech Stack

| Component | Version |
|------------|----------|
| .NET | 8.0 |
| ASP.NET Core Web API | — |
| JWT Library | System.IdentityModel.Tokens.Jwt (8.14.0) |
| Memory Cache | Microsoft.Extensions.Caching.Memory (9.0.10) |
| Redis Cache | Microsoft.Extensions.Caching.StackExchangeRedis (9.0.10) |

---

## ✅ Summary

This POC demonstrates a **production-ready JWT key rotation mechanism** supporting:

- 🔄 Seamless key rotation  
- 🧩 Multi-instance consistency  
- 🔐 Secure, auditable key storage  
- ⚡ High performance (cached validation)  
- 🧱 Simple configuration-based scaling  
