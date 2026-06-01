import { useEffect, useRef, useState } from "react"
import { RefreshCw, Warehouse, TrendingUp, Copy, Zap, Search, AlertTriangle, CheckCircle2, ShieldAlert } from "lucide-react"
import { cn } from "../lib/utils"

const WMS_URL = (import.meta.env.VITE_WMS_URL as string | undefined) ?? "http://localhost:8002"
const IS_URL = (import.meta.env.VITE_INTEGRATION_URL as string | undefined) ?? "http://localhost:8083"

interface WmsHealth {
  status: string
  mode: string
  reservations_processed: number
  duplicates_skipped: number
}

interface IntegrationHealth {
  status: string
  circuitBreakerState: string
  dlqDepth: number
  lastProcessedAt: string | null
}

interface Reservation {
  reservationId: string
  orderId: number
  stock: Record<string, number>
}

interface StockResult {
  productId: number
  quantity: number
}

type WmsMode = "normal" | "slow" | "down"

function Dot({ color }: { color: "green" | "red" | "yellow" | "gray" }) {
  return (
    <span className={cn(
      "h-2 w-2 rounded-full",
      color === "green" && "bg-emerald-500",
      color === "red" && "bg-red-500",
      color === "yellow" && "bg-amber-400",
      color === "gray" && "bg-slate-400",
    )} />
  )
}

function WmsModeBadge({ mode }: { mode: string }) {
  if (mode === "normal")
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-semibold text-emerald-800">
        <Dot color="green" />NORMAL
      </span>
    )
  if (mode === "slow")
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-2.5 py-1 text-xs font-semibold text-amber-800">
        <Dot color="yellow" />SLOW
      </span>
    )
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-red-100 px-2.5 py-1 text-xs font-semibold text-red-800">
      <Dot color="red" />DOWN
    </span>
  )
}

function CircuitBadge({ state }: { state: string }) {
  if (state === "CLOSED")
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-3 py-1.5 text-sm font-bold text-emerald-800">
        <CheckCircle2 size={14} />CLOSED
      </span>
    )
  if (state === "HALF_OPEN")
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-3 py-1.5 text-sm font-bold text-amber-800">
        <AlertTriangle size={14} />HALF-OPEN
      </span>
    )
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-red-100 px-3 py-1.5 text-sm font-bold text-red-800">
      <ShieldAlert size={14} />OPEN
    </span>
  )
}

interface MetricCardProps {
  label: string
  value: React.ReactNode
  icon: React.ReactNode
  sub?: string
  accent?: "default" | "warning" | "danger" | "success"
}

function MetricCard({ label, value, icon, sub, accent = "default" }: MetricCardProps) {
  return (
    <div className="relative overflow-hidden rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
      <div className={cn(
        "absolute inset-x-0 top-0 h-0.5",
        accent === "default" && "bg-slate-200",
        accent === "warning" && "bg-amber-400",
        accent === "danger" && "bg-red-500",
        accent === "success" && "bg-emerald-500",
      )} />
      <div className="flex items-start justify-between">
        <p className="text-sm font-medium text-slate-500">{label}</p>
        <span className={cn(
          "flex h-9 w-9 items-center justify-center rounded-lg",
          accent === "default" && "bg-slate-100 text-slate-500",
          accent === "warning" && "bg-amber-50 text-amber-600",
          accent === "danger" && "bg-red-50 text-red-600",
          accent === "success" && "bg-emerald-50 text-emerald-600",
        )}>
          {icon}
        </span>
      </div>
      <p className={cn(
        "mt-3 text-4xl font-bold tabular-nums tracking-tight",
        accent === "default" && "text-slate-900",
        accent === "warning" && "text-amber-600",
        accent === "danger" && "text-red-600",
        accent === "success" && "text-emerald-600",
      )}>
        {value}
      </p>
      {sub && <p className="mt-1 text-xs text-slate-400">{sub}</p>}
    </div>
  )
}

export function WmsPage() {
  const [health, setHealth] = useState<WmsHealth | null>(null)
  const [integrationHealth, setIntegrationHealth] = useState<IntegrationHealth | null>(null)
  const [reservations, setReservations] = useState<Reservation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [setting, setSetting] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [productId, setProductId] = useState("")
  const [stock, setStock] = useState<StockResult | null>(null)
  const [stockError, setStockError] = useState<string | null>(null)
  const [stockLoading, setStockLoading] = useState(false)
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const poll = async () => {
      try {
        const [h, r] = await Promise.all([
          fetch(`${WMS_URL}/health`).then((res) => res.json() as Promise<WmsHealth>),
          fetch(`${WMS_URL}/reservations`).then((res) =>
            res.json() as Promise<{ count: number; reservations: Reservation[] }>
          ),
        ])
        setHealth(h)
        setReservations(r.reservations)
        setError(null)
        setLastRefresh(new Date())
      } catch {
        setError("Cannot reach WMS stub — is it running on port 8002?")
      }

      try {
        const ih = await fetch(`${IS_URL}/health`).then((res) => res.json() as Promise<IntegrationHealth>)
        setIntegrationHealth(ih)
      } catch {
        setIntegrationHealth(null)
      }
    }
    void poll()
    const id = setInterval(() => void poll(), 3000)
    return () => clearInterval(id)
  }, [])

  const setMode = async (m: WmsMode) => {
    setSetting(true)
    try {
      await fetch(`${WMS_URL}/admin/mode`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ mode: m }),
      })
    } finally {
      setSetting(false)
    }
  }

  const resetState = async () => {
    setResetting(true)
    try {
      await fetch(`${WMS_URL}/admin/reset`, { method: "POST" })
      setStock(null)
    } finally {
      setResetting(false)
    }
  }

  const checkStock = async () => {
    const pid = parseInt(productId)
    if (isNaN(pid) || pid <= 0) { setStockError("Enter a valid product ID"); return }
    setStockLoading(true)
    setStockError(null)
    try {
      const data = await fetch(`${WMS_URL}/stock/${pid}`).then((r) => r.json() as Promise<StockResult>)
      setStock(data)
    } catch {
      setStockError("Failed to fetch stock")
    } finally {
      setStockLoading(false)
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent) => { if (e.key === "Enter") void checkStock() }
  const recentReservations = [...reservations].reverse().slice(0, 10)
  const circuitIsOpen = integrationHealth?.circuitBreakerState === "OPEN"
  const dlqHasItems = (integrationHealth?.dlqDepth ?? 0) > 0

  return (
    <div className="flex flex-col min-h-screen">
      {/* Page header */}
      <div className="border-b border-slate-200 bg-white px-8 py-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-violet-600 shadow-md shadow-violet-600/20">
              <Warehouse size={20} className="text-white" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-slate-900">WMS Stub</h1>
              <p className="text-sm text-slate-500">Warehouse Management System · port 8002</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {lastRefresh && (
              <span className="text-xs text-slate-400 flex items-center gap-1.5">
                <RefreshCw size={10} />
                {lastRefresh.toLocaleTimeString()}
              </span>
            )}
            {health ? <WmsModeBadge mode={health.mode} /> : (
              <span className="flex items-center gap-1.5 text-xs text-slate-400">
                <RefreshCw size={11} className="animate-spin" /> connecting…
              </span>
            )}
          </div>
        </div>
        {error && (
          <div className="mt-4 rounded-lg border border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-700">
            {error}
          </div>
        )}
      </div>

      <div className="flex-1 px-8 py-6 space-y-6">
        {/* Integration Service — QA-4 */}
        <div className={cn(
          "rounded-xl border shadow-sm overflow-hidden",
          circuitIsOpen ? "border-red-200" : dlqHasItems ? "border-amber-200" : "border-slate-200"
        )}>
          <div className={cn(
            "px-6 py-4 flex items-center justify-between",
            circuitIsOpen ? "bg-red-50 border-b border-red-100" :
            dlqHasItems ? "bg-amber-50 border-b border-amber-100" :
            "bg-slate-900 border-b border-slate-800"
          )}>
            <div>
              <h2 className={cn(
                "text-sm font-semibold",
                circuitIsOpen ? "text-red-900" : dlqHasItems ? "text-amber-900" : "text-white"
              )}>
                Integration Service
              </h2>
              <p className={cn(
                "text-xs mt-0.5",
                circuitIsOpen ? "text-red-600" : dlqHasItems ? "text-amber-600" : "text-slate-400"
              )}>
                Circuit breaker · DLQ depth · QA-4 observability
              </p>
            </div>
            {integrationHealth ? (
              <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-100 px-2.5 py-1 text-xs font-semibold text-emerald-800">
                <span className="relative flex h-2 w-2">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-60" />
                  <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
                </span>
                ONLINE
              </span>
            ) : (
              <span className="flex items-center gap-1.5 text-xs text-slate-400">
                <RefreshCw size={11} className="animate-spin" /> connecting…
              </span>
            )}
          </div>
          <div className={cn(
            "grid grid-cols-3 divide-x bg-white",
            circuitIsOpen ? "divide-red-100" : dlqHasItems ? "divide-amber-100" : "divide-slate-100"
          )}>
            <div className="px-6 py-5">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">Circuit Breaker</p>
              {integrationHealth
                ? <CircuitBadge state={integrationHealth.circuitBreakerState} />
                : <span className="text-2xl font-bold text-slate-300">—</span>}
              <p className="mt-2 text-xs text-slate-400">Polly policy (3 failures → OPEN)</p>
            </div>
            <div className="px-6 py-5">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">DLQ Depth</p>
              <p className={cn(
                "text-3xl font-bold tabular-nums",
                integrationHealth && integrationHealth.dlqDepth > 0 ? "text-red-600" : "text-slate-900"
              )}>
                {integrationHealth?.dlqDepth ?? "—"}
              </p>
              <p className="mt-2 text-xs text-slate-400">messages pending reconciliation</p>
            </div>
            <div className="px-6 py-5">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-400 mb-3">Last Processed</p>
              <p className="text-sm font-semibold text-slate-700">
                {integrationHealth?.lastProcessedAt
                  ? new Date(integrationHealth.lastProcessedAt).toLocaleTimeString()
                  : <span className="text-slate-300 text-2xl font-bold">—</span>}
              </p>
              <p className="mt-2 text-xs text-slate-400">last successful event</p>
            </div>
          </div>
        </div>

        {/* Metric cards */}
        <div className="grid grid-cols-4 gap-4">
          <MetricCard
            label="Reservations Processed"
            value={health?.reservations_processed ?? "—"}
            icon={<TrendingUp size={16} />}
            sub="total since last reset"
          />
          <MetricCard
            label="Duplicates Skipped"
            value={health?.duplicates_skipped ?? "—"}
            icon={<Copy size={16} />}
            sub="zero double-deductions (QA-3)"
            accent={health && health.duplicates_skipped > 0 ? "warning" : "default"}
          />
          <MetricCard
            label="Service Status"
            value={health ? health.status.toUpperCase() : "—"}
            icon={<Zap size={16} />}
            sub={health?.mode === "down" ? "returning 503" : health?.mode === "slow" ? "10s artificial delay" : "accepting requests"}
            accent={health?.status === "ok" ? "success" : "danger"}
          />
          <div className="relative overflow-hidden rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
            <div className={cn(
              "absolute inset-x-0 top-0 h-0.5",
              health?.mode === "down" ? "bg-red-500" : health?.mode === "slow" ? "bg-amber-400" : "bg-emerald-500"
            )} />
            <p className="text-sm font-medium text-slate-500">Current Mode</p>
            <div className="mt-3">
              {health ? <WmsModeBadge mode={health.mode} /> : <p className="text-4xl font-bold text-slate-300">—</p>}
            </div>
            <p className="mt-2 text-xs text-slate-400">
              {health?.mode === "slow" ? "Triggers circuit breaker (QA-4)" :
               health?.mode === "down" ? "All requests return 503" : "Normal operation"}
            </p>
          </div>
        </div>

        {/* Bottom row: Failure Injection + Stock Lookup */}
        <div className="grid grid-cols-2 gap-6">
          {/* Failure Injection */}
          <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
            <div className="border-b border-slate-100 bg-slate-50 px-6 py-4">
              <h2 className="text-sm font-semibold text-slate-700">Failure Injection</h2>
              <p className="text-xs text-slate-400 mt-0.5">Simulate WMS degradation for circuit breaker testing</p>
            </div>
            <div className="px-6 py-4 space-y-3">
              <div className="flex gap-2">
                <button
                  onClick={() => void setMode("normal")}
                  disabled={setting || health?.mode === "normal"}
                  className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-3 py-2 text-sm font-medium text-white shadow-sm hover:bg-emerald-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                >
                  <span className="h-1.5 w-1.5 rounded-full bg-emerald-300" />
                  Normal
                </button>
                <button
                  onClick={() => void setMode("slow")}
                  disabled={setting || health?.mode === "slow"}
                  className="inline-flex items-center gap-2 rounded-lg bg-amber-500 px-3 py-2 text-sm font-medium text-white shadow-sm hover:bg-amber-600 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                >
                  <span className="h-1.5 w-1.5 rounded-full bg-amber-200" />
                  Slow — 10s
                </button>
                <button
                  onClick={() => void setMode("down")}
                  disabled={setting || health?.mode === "down"}
                  className="inline-flex items-center gap-2 rounded-lg bg-red-600 px-3 py-2 text-sm font-medium text-white shadow-sm hover:bg-red-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
                >
                  <span className="h-1.5 w-1.5 rounded-full bg-red-300" />
                  Down — 503
                </button>
              </div>
              <button
                onClick={() => void resetState()}
                disabled={resetting}
                className="w-full rounded-lg border border-slate-300 bg-white py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors shadow-sm"
              >
                {resetting ? "Resetting…" : "Reset State"}
              </button>
            </div>
          </div>

          {/* Stock Lookup */}
          <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
            <div className="border-b border-slate-100 bg-slate-50 px-6 py-4">
              <h2 className="text-sm font-semibold text-slate-700">Stock Lookup</h2>
              <p className="text-xs text-slate-400 mt-0.5">Check current stock level for any product ID</p>
            </div>
            <div className="px-6 py-4">
              <div className="flex gap-2">
                <input
                  ref={inputRef}
                  type="number"
                  min={1}
                  placeholder="Product ID"
                  value={productId}
                  onChange={(e) => setProductId(e.target.value)}
                  onKeyDown={handleKeyDown}
                  className="w-36 rounded-lg border border-slate-300 px-3 py-2 text-sm focus:border-slate-500 focus:outline-none focus:ring-2 focus:ring-slate-200"
                />
                <button
                  onClick={() => void checkStock()}
                  disabled={stockLoading || !productId}
                  className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors shadow-sm"
                >
                  {stockLoading ? <RefreshCw size={13} className="animate-spin" /> : <Search size={13} />}
                  Check
                </button>
              </div>
              {stockError && <p className="mt-2 text-xs text-red-600">{stockError}</p>}
              {stock && !stockError && (
                <div className="mt-3 flex items-baseline gap-2.5 rounded-xl bg-slate-50 border border-slate-200 px-4 py-3">
                  <span className="text-sm text-slate-500">Product #{stock.productId}</span>
                  <span className={cn(
                    "text-3xl font-bold tabular-nums",
                    stock.quantity === 0 ? "text-red-600" :
                    stock.quantity < 10 ? "text-amber-600" : "text-emerald-600"
                  )}>
                    {stock.quantity}
                  </span>
                  <span className="text-sm text-slate-500">units in stock</span>
                  {stock.quantity === 0 && (
                    <span className="ml-auto text-xs font-semibold text-red-600 bg-red-50 px-2 py-0.5 rounded-full">OUT OF STOCK</span>
                  )}
                  {stock.quantity > 0 && stock.quantity < 10 && (
                    <span className="ml-auto text-xs font-semibold text-amber-600 bg-amber-50 px-2 py-0.5 rounded-full">LOW STOCK</span>
                  )}
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Reservations table */}
        <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
          <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
            <div>
              <h2 className="text-sm font-semibold text-slate-700">Recent Reservations</h2>
              <p className="text-xs text-slate-400 mt-0.5">Last 10 stock reservations processed</p>
            </div>
            <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-500">
              {reservations.length} total
            </span>
          </div>
          {recentReservations.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-slate-400">
              <Warehouse size={32} className="mb-3 text-slate-300" />
              <p className="text-sm font-medium">No reservations processed yet</p>
              <p className="text-xs mt-1">Reservations will appear when the Integration Service sends orders</p>
            </div>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50 text-left">
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Reservation ID</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Order</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Products</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400 text-right">Stock After</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {recentReservations.map((r) => {
                  const productIds = Object.keys(r.stock)
                  return (
                    <tr key={r.reservationId} className="hover:bg-slate-50 transition-colors">
                      <td className="px-6 py-3.5 font-mono text-[11px] text-slate-400">{r.reservationId.slice(0, 8)}…</td>
                      <td className="px-6 py-3.5 font-semibold text-slate-900">#{r.orderId}</td>
                      <td className="px-6 py-3.5">
                        <span className="rounded-full bg-violet-50 px-2 py-0.5 text-xs font-medium text-violet-700">
                          {productIds.length} product{productIds.length !== 1 ? "s" : ""}
                        </span>
                      </td>
                      <td className="px-6 py-3.5 text-right font-mono text-[11px] text-slate-400">
                        {productIds.map((pid) => `#${pid}: ${r.stock[pid]}`).join(" · ")}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
}
