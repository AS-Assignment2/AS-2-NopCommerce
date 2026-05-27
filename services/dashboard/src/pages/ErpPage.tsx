import { useEffect, useState } from "react"
import { RefreshCw, Package, TrendingUp, Copy, Zap } from "lucide-react"
import { cn } from "../lib/utils"

const ERP_URL = (import.meta.env.VITE_ERP_URL as string | undefined) ?? "http://localhost:8001"

interface ErpHealth {
  status: string
  mode: string
  orders_received: number
  duplicates_skipped: number
}

interface OrderItem {
  productId: number
  sku: string
  quantity: number
}

interface Order {
  eventId: string
  orderId: number
  customerId: number
  items: OrderItem[]
  totalAmount: number
  occurredAt: string
}

type ErpMode = "normal" | "down"

function Badge({ children, variant }: { children: React.ReactNode; variant: "success" | "danger" | "warning" | "neutral" }) {
  return (
    <span className={cn(
      "inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold",
      variant === "success" && "bg-emerald-100 text-emerald-800",
      variant === "danger" && "bg-red-100 text-red-800",
      variant === "warning" && "bg-amber-100 text-amber-800",
      variant === "neutral" && "bg-slate-100 text-slate-600",
    )}>
      {children}
    </span>
  )
}

function Dot({ color }: { color: "green" | "red" | "yellow" }) {
  return (
    <span className={cn(
      "h-2 w-2 rounded-full",
      color === "green" && "bg-emerald-500",
      color === "red" && "bg-red-500",
      color === "yellow" && "bg-amber-400",
    )} />
  )
}

function ModeBadge({ mode }: { mode: string }) {
  if (mode === "normal") return <Badge variant="success"><Dot color="green" />NORMAL</Badge>
  return <Badge variant="danger"><Dot color="red" />DOWN</Badge>
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

export function ErpPage() {
  const [health, setHealth] = useState<ErpHealth | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [error, setError] = useState<string | null>(null)
  const [setting, setSetting] = useState(false)
  const [resetting, setResetting] = useState(false)
  const [lastRefresh, setLastRefresh] = useState<Date | null>(null)

  useEffect(() => {
    const poll = async () => {
      try {
        const [h, o] = await Promise.all([
          fetch(`${ERP_URL}/health`).then((r) => r.json() as Promise<ErpHealth>),
          fetch(`${ERP_URL}/orders`).then((r) => r.json() as Promise<{ count: number; orders: Order[] }>),
        ])
        setHealth(h)
        setOrders(o.orders)
        setError(null)
        setLastRefresh(new Date())
      } catch {
        setError("Cannot reach ERP stub — is it running on port 8001?")
      }
    }
    void poll()
    const id = setInterval(() => void poll(), 3000)
    return () => clearInterval(id)
  }, [])

  const setMode = async (m: ErpMode) => {
    setSetting(true)
    try {
      await fetch(`${ERP_URL}/admin/mode`, {
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
      await fetch(`${ERP_URL}/admin/reset`, { method: "POST" })
    } finally {
      setResetting(false)
    }
  }

  const recentOrders = [...orders].reverse().slice(0, 10)

  return (
    <div className="flex flex-col min-h-screen">
      {/* Page header */}
      <div className="border-b border-slate-200 bg-white px-8 py-6">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <div className="flex h-11 w-11 items-center justify-center rounded-xl bg-blue-600 shadow-md shadow-blue-600/20">
              <Package size={20} className="text-white" />
            </div>
            <div>
              <h1 className="text-xl font-bold text-slate-900">ERP Stub</h1>
              <p className="text-sm text-slate-500">Enterprise Resource Planning · port 8001</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            {lastRefresh && (
              <span className="text-xs text-slate-400 flex items-center gap-1.5">
                <RefreshCw size={10} />
                {lastRefresh.toLocaleTimeString()}
              </span>
            )}
            {health ? <ModeBadge mode={health.mode} /> : (
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

      {/* Content */}
      <div className="flex-1 px-8 py-6 space-y-6">
        {/* Metric cards */}
        <div className="grid grid-cols-4 gap-4">
          <MetricCard
            label="Orders Received"
            value={health?.orders_received ?? "—"}
            icon={<TrendingUp size={16} />}
            sub="total since last reset"
            accent="default"
          />
          <MetricCard
            label="Duplicates Skipped"
            value={health?.duplicates_skipped ?? "—"}
            icon={<Copy size={16} />}
            sub="idempotency guard (QA-5)"
            accent={health && health.duplicates_skipped > 0 ? "warning" : "default"}
          />
          <MetricCard
            label="Service Status"
            value={health ? health.status.toUpperCase() : "—"}
            icon={<Zap size={16} />}
            sub={health?.mode === "down" ? "returning 503" : "accepting requests"}
            accent={health?.status === "ok" ? "success" : "danger"}
          />
          <div className="relative overflow-hidden rounded-xl border border-slate-200 bg-white p-6 shadow-sm">
            <div className={cn(
              "absolute inset-x-0 top-0 h-0.5",
              health?.mode === "down" ? "bg-red-500" : "bg-emerald-500"
            )} />
            <p className="text-sm font-medium text-slate-500">Current Mode</p>
            <div className="mt-3">
              {health ? <ModeBadge mode={health.mode} /> : <p className="text-4xl font-bold text-slate-300">—</p>}
            </div>
            <p className="mt-2 text-xs text-slate-400">
              {health?.mode === "down" ? "All requests return 503" : "Accepting all requests"}
            </p>
          </div>
        </div>

        {/* Failure Injection */}
        <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
          <div className="border-b border-slate-100 bg-slate-50 px-6 py-4">
            <h2 className="text-sm font-semibold text-slate-700">Failure Injection</h2>
            <p className="text-xs text-slate-400 mt-0.5">Simulate ERP unavailability for integration testing</p>
          </div>
          <div className="px-6 py-4 flex items-center gap-3">
            <button
              onClick={() => void setMode("normal")}
              disabled={setting || health?.mode === "normal"}
              className="inline-flex items-center gap-2 rounded-lg bg-emerald-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-emerald-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
            >
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-300" />
              Normal
            </button>
            <button
              onClick={() => void setMode("down")}
              disabled={setting || health?.mode === "down"}
              className="inline-flex items-center gap-2 rounded-lg bg-red-600 px-4 py-2 text-sm font-medium text-white shadow-sm hover:bg-red-700 disabled:opacity-40 disabled:cursor-not-allowed transition-all"
            >
              <span className="h-1.5 w-1.5 rounded-full bg-red-300" />
              Down — 503
            </button>
            <div className="ml-auto">
              <button
                onClick={() => void resetState()}
                disabled={resetting}
                className="rounded-lg border border-slate-300 bg-white px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors shadow-sm"
              >
                {resetting ? "Resetting…" : "Reset State"}
              </button>
            </div>
          </div>
        </div>

        {/* Orders table */}
        <div className="rounded-xl border border-slate-200 bg-white shadow-sm overflow-hidden">
          <div className="flex items-center justify-between border-b border-slate-100 px-6 py-4">
            <div>
              <h2 className="text-sm font-semibold text-slate-700">Recent Orders</h2>
              <p className="text-xs text-slate-400 mt-0.5">Last 10 orders received by this stub</p>
            </div>
            <span className="rounded-full bg-slate-100 px-2.5 py-1 text-xs font-medium text-slate-500">
              {orders.length} total
            </span>
          </div>
          {recentOrders.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 text-slate-400">
              <Package size={32} className="mb-3 text-slate-300" />
              <p className="text-sm font-medium">No orders received yet</p>
              <p className="text-xs mt-1">Orders will appear here when the Integration Service sends them</p>
            </div>
          ) : (
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 bg-slate-50 text-left">
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Order</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Event ID</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Customer</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400">Items</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400 text-right">Total</th>
                  <th className="px-6 py-3 text-xs font-semibold uppercase tracking-wide text-slate-400 text-right">Received</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {recentOrders.map((o) => (
                  <tr key={o.eventId} className="hover:bg-slate-50 transition-colors">
                    <td className="px-6 py-3.5 font-semibold text-slate-900">#{o.orderId}</td>
                    <td className="px-6 py-3.5 font-mono text-[11px] text-slate-400">{o.eventId.slice(0, 12)}…</td>
                    <td className="px-6 py-3.5 text-slate-600">Customer #{o.customerId}</td>
                    <td className="px-6 py-3.5">
                      <span className="rounded-full bg-blue-50 px-2 py-0.5 text-xs font-medium text-blue-700">
                        {o.items.length} item{o.items.length !== 1 ? "s" : ""}
                      </span>
                    </td>
                    <td className="px-6 py-3.5 text-right font-semibold text-slate-900">€{o.totalAmount.toFixed(2)}</td>
                    <td className="px-6 py-3.5 text-right text-xs text-slate-400">{new Date(o.occurredAt).toLocaleTimeString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </div>
      </div>
    </div>
  )
}
