import { useEffect, useState } from "react"
import { RefreshCw } from "lucide-react"

const ERP_URL = (import.meta.env.VITE_ERP_URL as string | undefined) ?? "http://localhost:8001"

interface ErpHealth {
  status: string
  mode: string
  orders_received: number
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

function ModeBadge({ mode }: { mode: string }) {
  if (mode === "normal") {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">
        <span className="h-2 w-2 rounded-full bg-green-500" />
        NORMAL
      </span>
    )
  }
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full bg-red-100 px-3 py-1 text-xs font-semibold text-red-800">
      <span className="h-2 w-2 rounded-full bg-red-500" />
      DOWN
    </span>
  )
}

export function ErpPage() {
  const [health, setHealth] = useState<ErpHealth | null>(null)
  const [orders, setOrders] = useState<Order[]>([])
  const [error, setError] = useState<string | null>(null)
  const [setting, setSetting] = useState(false)

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

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">ERP Stub</h1>
          <p className="mt-0.5 text-sm text-gray-500">Simulates ERP order acceptance · port 8001</p>
        </div>
        <div className="flex items-center gap-3 pt-1">
          {health ? <ModeBadge mode={health.mode} /> : (
            <span className="inline-flex items-center gap-1.5 text-xs text-gray-400">
              <RefreshCw size={12} className="animate-spin" /> connecting…
            </span>
          )}
        </div>
      </div>

      {error && (
        <div className="rounded-lg border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          {error}
        </div>
      )}

      {/* Stats */}
      <div className="grid grid-cols-2 gap-4">
        <div className="rounded-xl border bg-white p-6 shadow-sm">
          <p className="text-sm font-medium text-gray-500">Orders Received</p>
          <p className="mt-2 text-5xl font-bold tabular-nums text-gray-900">
            {health?.orders_received ?? "—"}
          </p>
        </div>
        <div className="rounded-xl border bg-white p-6 shadow-sm">
          <p className="text-sm font-medium text-gray-500">Service Status</p>
          <p className={`mt-2 text-5xl font-bold ${health?.status === "ok" ? "text-green-600" : "text-red-600"}`}>
            {health ? health.status.toUpperCase() : "—"}
          </p>
        </div>
      </div>

      {/* Failure Injection */}
      <div className="rounded-xl border bg-white p-6 shadow-sm">
        <h2 className="mb-1 text-sm font-semibold text-gray-700">Failure Injection</h2>
        <p className="mb-4 text-xs text-gray-400">Change how the ERP stub responds to incoming requests</p>
        <div className="flex gap-2">
          <button
            onClick={() => void setMode("normal")}
            disabled={setting || health?.mode === "normal"}
            className="rounded-lg px-4 py-2 text-sm font-medium bg-green-600 text-white hover:bg-green-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            Normal
          </button>
          <button
            onClick={() => void setMode("down")}
            disabled={setting || health?.mode === "down"}
            className="rounded-lg px-4 py-2 text-sm font-medium bg-red-600 text-white hover:bg-red-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            Down — 503
          </button>
        </div>
      </div>

      {/* Orders Table */}
      <div className="rounded-xl border bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b flex items-center justify-between">
          <h2 className="text-sm font-semibold text-gray-700">Recent Orders</h2>
          <span className="text-xs text-gray-400">last 10 · auto-refresh 3s</span>
        </div>
        {orders.length === 0 ? (
          <p className="px-6 py-10 text-center text-sm text-gray-400">
            No orders received yet
          </p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-gray-50 text-left">
                <th className="px-6 py-3 font-medium text-gray-500">Order ID</th>
                <th className="px-6 py-3 font-medium text-gray-500">Customer</th>
                <th className="px-6 py-3 font-medium text-gray-500">Items</th>
                <th className="px-6 py-3 font-medium text-gray-500 text-right">Total</th>
                <th className="px-6 py-3 font-medium text-gray-500 text-right">Time</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {[...orders].reverse().slice(0, 10).map((o) => (
                <tr key={o.eventId} className="hover:bg-gray-50 transition-colors">
                  <td className="px-6 py-3 font-mono text-gray-900">#{o.orderId}</td>
                  <td className="px-6 py-3 text-gray-600">{o.customerId}</td>
                  <td className="px-6 py-3 text-gray-600">
                    {o.items.length} item{o.items.length !== 1 ? "s" : ""}
                  </td>
                  <td className="px-6 py-3 text-right font-medium text-gray-900">
                    €{o.totalAmount.toFixed(2)}
                  </td>
                  <td className="px-6 py-3 text-right text-gray-400">
                    {new Date(o.occurredAt).toLocaleTimeString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
