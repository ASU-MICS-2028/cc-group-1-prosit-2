// HealthConnect patient-record service (Lab 6, Part D: audit logging), grown from the lab's
// Express starter. What it adds to the starter:
//   - every access is logged INSIDE the same transaction as the access, so a record can never
//     be read or changed without its audit entry (if the audit insert fails, the request fails)
//   - denied and not-found attempts are logged too, with the caller's role and outcome
//   - each request runs as the caller's database role (db/rbac.sql), so Postgres itself
//     enforces who may see what; the API cannot leak another district's patients by mistake
//   - consent is required before a patient record is created (Act 843 s.20, s.37(2)(b))
//   - the database password is read from Secrets Manager with the server's IAM role, and
//     re-read automatically after a rotation
//
// Identity comes from demo headers (x-user-id, x-user-role, x-user-district). In the real API
// these come from a verified JWT (Bernard's auth service); never trust them from a client.

const express = require("express")
const { Pool } = require("pg")
const fs = require("fs")
const { SecretsManagerClient, GetSecretValueCommand } = require("@aws-sdk/client-secrets-manager")

const ROLES = {
  chv: "hc_chv",
  clinician: "hc_clinician",
  ghs_official: "hc_ghs_official",
  auditor: "hc_auditor",
}
// Lab starter sends "F"/"Twi"; FHIR wants coded values.
const GENDER = { f: "female", female: "female", m: "male", male: "male", other: "other", unknown: "unknown" }
const LANGUAGE = { en: "en", english: "en", tw: "tw", twi: "tw", ee: "ee", ewe: "ee", dag: "dag", dagbani: "dag", ha: "ha", hausa: "ha" }
const VITALS = [
  ["systolic", "systolic_bp", "8480-6", "mm[Hg]"],
  ["diastolic", "diastolic_bp", "8462-4", "mm[Hg]"],
  ["temperature", "body_temperature", "8310-5", "Cel"],
  ["pulse", "heart_rate", "8867-4", "/min"],
  ["weight", "body_weight", "29463-7", "kg"],
  ["height", "body_height", "8302-2", "cm"],
]

const secrets = new SecretsManagerClient({ region: process.env.AWS_REGION })
let pool

async function connect() {
  const { SecretString, VersionId } = await secrets.send(new GetSecretValueCommand({ SecretId: process.env.DB_SECRET_ARN }))
  const { username, password } = JSON.parse(SecretString)
  console.log(`${new Date().toISOString()} database credentials loaded (secret version ${VersionId.slice(0, 8)})`)
  if (pool) await pool.end().catch(() => {})
  pool = new Pool({
    host: process.env.DB_HOST,
    database: process.env.DB_NAME,
    user: username,
    password,
    ssl: { ca: fs.readFileSync(process.env.DB_SSL_CA, "utf8"), rejectUnauthorized: true },
    max: 5,
  })
}

class HttpError extends Error {
  constructor(status, message, outcome) {
    super(message)
    this.status = status
    this.outcome = outcome
  }
}

function caller(req) {
  const role = ROLES[req.get("x-user-role")]
  const id = req.get("x-user-id")
  if (!role || !id) throw new HttpError(401, "x-user-id and a known x-user-role are required", "denied")
  return { id, role, district: req.get("x-user-district") ?? "" }
}

async function audit(client, who, patientId, action, outcome, purpose, ip) {
  await client.query(
    `INSERT INTO access_log (accessed_by, role, patient_id, action, outcome, purpose_of_use, source_ip)
     VALUES ($1, $2, $3, $4, $5, $6, $7)`,
    [who.id, who.role, patientId, action, outcome, purpose, ip]
  )
}

/**
 * Runs `work` as the caller's database role and writes its audit entry in the same
 * transaction. Denied or failed attempts are rolled back and then logged on their own.
 */
async function asCaller(req, res, { action, purpose = "TREAT", patientId = null }, work) {
  let who
  try {
    who = caller(req)
  } catch (e) {
    return res.status(e.status).json({ error: e.message })
  }
  const ip = req.ip?.replace(/^::ffff:/, "") || null
  for (let attempt = 1; attempt <= 2; attempt++) {
    let client
    try {
      client = await pool.connect()
    } catch (e) {
      // 28P01: password rejected, e.g. just rotated. Fetch the new one once and retry.
      if (e.code === "28P01" && attempt === 1) { await connect(); continue }
      throw e
    }
    try {
      await client.query("BEGIN")
      await client.query(`SET LOCAL ROLE ${who.role}`)
      await client.query("SELECT set_config('app.user_id', $1, true), set_config('app.district', $2, true)", [who.id, who.district])
      const { status, body, patientId: pid } = await work(client)
      await audit(client, who, pid ?? patientId, action, "success", purpose, ip)
      await client.query("COMMIT")
      return res.status(status).json(body)
    } catch (e) {
      await client.query("ROLLBACK").catch(() => {})
      const http =
        e instanceof HttpError ? e
        : e.code === "42501" ? new HttpError(403, "not allowed for your role", "denied") // insufficient_privilege / RLS
        : e.code === "23514" || e.code === "P0001" || e.code === "22P02" ? new HttpError(422, e.message, "error")
        : new HttpError(500, "internal error", "error")
      if (http.status === 500) console.error(e)
      // The failed attempt is still logged, in its own transaction, as the same role.
      try {
        await client.query("BEGIN")
        await client.query(`SET LOCAL ROLE ${who.role}`)
        await audit(client, who, e.patientId ?? patientId, action, http.outcome, purpose, ip)
        await client.query("COMMIT")
      } catch (logError) {
        await client.query("ROLLBACK").catch(() => {})
        console.error("audit write failed", logError)
      }
      return res.status(http.status).json({ error: http.message })
    } finally {
      client.release()
    }
  }
}

const app = express()
app.use(express.json())
app.set("trust proxy", "loopback")

app.get("/health", (_req, res) => res.json({ ok: true }))

// Lab starter's POST /patients, with consent required and values coded for FHIR.
app.post("/patients", (req, res) =>
  asCaller(req, res, { action: "write" }, async (db) => {
    const b = req.body ?? {}
    if (!["spoken", "thumbprint", "signature"].includes(b.consent_method))
      throw new HttpError(422, "consent_method (spoken, thumbprint or signature) is required before a record is created (Act 843 s.20)", "error")
    const gender = GENDER[String(b.gender ?? "").toLowerCase()]
    const language = LANGUAGE[String(b.preferred_language ?? "").toLowerCase()]
    const { rows } = await db.query(
      `INSERT INTO patients (full_name, phone, date_of_birth, gender, region, district, preferred_language,
                             consent_given_at, consent_method)
       VALUES ($1, $2, $3, $4, $5, coalesce($6, current_setting('app.district')), $7, now(), $8)
       RETURNING id, fhir_id, full_name, gender, district, preferred_language, consent_method, created_at`,
      [b.full_name, b.phone ?? null, b.date_of_birth ?? null, gender ?? null, b.region ?? null, b.district ?? null, language ?? null, b.consent_method]
    )
    const p = rows[0]
    await db.query("INSERT INTO consents (patient_id, status, method, obtained_by) VALUES ($1, 'active', $2, current_setting('app.user_id'))", [p.id, b.consent_method])
    await db.query("INSERT INTO patient_identifiers (patient_id, system, value) VALUES ($1, 'urn:healthconnect-gh:mrn', $2)", [p.id, `HC-${String(p.id).padStart(6, "0")}`])
    return { status: 201, body: p, patientId: p.id }
  })
)

// Lab starter's GET /patients/:id. Row-level security hides other districts' patients, so for a
// CHV they look exactly like a missing record (404), and the attempt is logged.
app.get("/patients/:id", (req, res) =>
  asCaller(req, res, { action: "read", patientId: Number(req.params.id) || null }, async (db) => {
    const { rows } = await db.query(
      "SELECT id, fhir_id, full_name, phone, date_of_birth, gender, region, district, preferred_language, consent_given_at, consent_method FROM patients WHERE id = $1",
      [req.params.id]
    )
    if (rows.length === 0) throw new HttpError(404, "patient not found", "not-found")
    return { status: 200, body: rows[0] }
  })
)

// Record vitals: one coded observation per value, BP pair sharing a panel.
app.post("/patients/:id/observations", (req, res) =>
  asCaller(req, res, { action: "write", patientId: Number(req.params.id) || null }, async (db) => {
    const found = await db.query("SELECT 1 FROM patients WHERE id = $1", [req.params.id])
    if (found.rowCount === 0) throw new HttpError(404, "patient not found", "not-found")
    const panel = req.body?.systolic != null && req.body?.diastolic != null ? (await db.query("SELECT gen_random_uuid() AS u")).rows[0].u : null
    const saved = []
    for (const [field, type, loinc, unit] of VITALS) {
      if (req.body?.[field] == null) continue
      const { rows } = await db.query(
        `INSERT INTO observations (patient_id, observation_type, loinc_code, value, unit, panel_id, recorded_by)
         VALUES ($1, $2, $3, $4, $5, $6, current_setting('app.user_id'))
         RETURNING id, loinc_code, value_quantity, unit`,
        [req.params.id, type, loinc, String(req.body[field]), unit, loinc.startsWith("84") ? panel : null]
      )
      saved.push(rows[0])
    }
    if (saved.length === 0) throw new HttpError(422, "send at least one of: " + VITALS.map((v) => v[0]).join(", "), "error")
    return { status: 201, body: saved }
  })
)

app.get("/patients/:id/observations", (req, res) =>
  asCaller(req, res, { action: "read", patientId: Number(req.params.id) || null }, async (db) => {
    const { rows } = await db.query(
      "SELECT id, loinc_code, observation_type, value_quantity, unit, status, recorded_by, recorded_at FROM observations WHERE patient_id = $1 ORDER BY recorded_at DESC",
      [req.params.id]
    )
    if (rows.length === 0) {
      const found = await db.query("SELECT 1 FROM patients WHERE id = $1", [req.params.id])
      if (found.rowCount === 0) throw new HttpError(404, "patient not found", "not-found")
    }
    return { status: 200, body: rows }
  })
)

// Anonymised district counts for GHS officials (purpose: public health).
app.get("/surveillance/weekly", (req, res) =>
  asCaller(req, res, { action: "read", purpose: "PUBHLTH" }, async (db) => {
    const { rows } = await db.query("SELECT * FROM surveillance_weekly ORDER BY district, week")
    return { status: 200, body: rows }
  })
)

// The audit trail itself, for the auditor role only (its reads are audited too).
app.get("/audit", (req, res) =>
  asCaller(req, res, { action: "read", purpose: "HOPERAT" }, async (db) => {
    const { rows } = await db.query(
      "SELECT id, accessed_by, role, patient_id, action, outcome, purpose_of_use, accessed_at FROM access_log ORDER BY id DESC LIMIT $1",
      [Math.min(Number(req.query.limit) || 10, 100)]
    )
    const chain = (await db.query("SELECT * FROM verify_access_log()")).rows[0]
    return { status: 200, body: { chain_intact: chain.first_bad_id === null, entries_checked: Number(chain.entries_checked), entries: rows } }
  })
)

const PORT = process.env.PORT || 3000
connect().then(() =>
  app.listen(PORT, () => console.log(`healthconnect-patient-service listening on port ${PORT}`))
)
