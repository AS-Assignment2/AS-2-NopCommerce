import { useEffect, useRef, useState } from "react"
import { RefreshCw, Search } from "lucide-react"

const WMS_URL = (import.meta.env.VITE_WMS_URL as string | undefined) ?? "http://localhost:8002"

interface WmsHealth {
  status: string
  mode: string
  reservations_processed: number
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

function ModeBadge({ mode }: { mode: string }) {
  if (mode === "normal") {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-green-100 px-3 py-1 text-xs font-semibold text-green-800">
        <span className="h-2 w-2 rounded-full bg-green-500" />
        NORMAL
      </span>
    )
  }
  if (mode === "slow") {
    return (
      <span className="inline-flex items-center gap-1.5 rounded-full bg-yellow-100 px-3 py-1 text-xs font-semibold text-yellow-800">
        <span className="h-2 w-2 rounded-full bg-yellow-400" />
        SLOW
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

export function WmsPage() {
  const [health, setHealth] = useState<WmsHealth | null>(null)
  const [reservations, setReservations] = useState<Reservation[]>([])
  const [error, setError] = useState<string | null>(null)
  const [setting, setSetting] = useState(false)
  const [productId, setProductId] = useState("")
  const [stock, setStock] = useState<StockResult | null>(null)
  const [stockError, setStockError] = useState<string | null>(null)
  const [stockLoading, setStockLoading] = useState(false)
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
      } catch {
        setError("Cannot reach WMS stub — is it running on port 8002?")
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

  const checkStock = async () => {
    const pid = parseInt(productId)
    if (isNaN(pid) || pid <= 0) {
      setStockError("Enter a valid product ID")
      return
    }
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

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === "Enter") void checkStock()
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">WMS Stub</h1>
          <p className="mt-0.5 text-sm text-gray-500">Simulates warehouse management · port 8002</p>
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
          <p className="text-sm font-medium text-gray-500">Reservations Processed</p>
          <p className="mt-2 text-5xl font-bold tabular-nums text-gray-900">
            {health?.reservations_processed ?? "—"}
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
        <p className="mb-4 text-xs text-gray-400">Change how the WMS stub responds to reservation requests</p>
        <div className="flex gap-2">
          <button
            onClick={() => void setMode("normal")}
            disabled={setting || health?.mode === "normal"}
            className="rounded-lg px-4 py-2 text-sm font-medium bg-green-600 text-white hover:bg-green-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            Normal
          </button>
          <button
            onClick={() => void setMode("slow")}
            disabled={setting || health?.mode === "slow"}
            className="rounded-lg px-4 py-2 text-sm font-medium bg-yellow-500 text-white hover:bg-yellow-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            Slow — 10s delay
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

      {/* Stock Lookup */}
      <div className="rounded-xl border bg-white p-6 shadow-sm">
        <h2 className="mb-1 text-sm font-semibold text-gray-700">Stock Lookup</h2>
        <p className="mb-4 text-xs text-gray-400">Check current stock level for any product</p>
        <div className="flex gap-2">
          <input
            ref={inputRef}
            type="number"
            min={1}
            placeholder="Product ID"
            value={productId}
            onChange={(e) => setProductId(e.target.value)}
            onKeyDown={handleKeyDown}
            className="w-36 rounded-lg border border-gray-300 px-3 py-2 text-sm focus:border-gray-500 focus:outline-none focus:ring-1 focus:ring-gray-500"
          />
          <button
            onClick={() => void checkStock()}
            disabled={stockLoading || !productId}
            className="inline-flex items-center gap-1.5 rounded-lg border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
          >
            <Search size={14} />
            Check
          </button>
        </div>
        {stockError && <p className="mt-2 text-xs text-red-600">{stockError}</p>}
        {stock && !stockError && (
          <div className="mt-3 inline-flex items-baseline gap-2 rounded-lg bg-gray-50 px-4 py-2.5">
            <span className="text-sm text-gray-500">Product #{stock.productId}</span>
            <span className="text-2xl font-bold tabular-nums text-gray-900">{stock.quantity}</span>
            <span className="text-sm text-gray-500">units</span>
          </div>
        )}
      </div>

      {/* Reservations Table */}
      <div className="rounded-xl border bg-white shadow-sm overflow-hidden">
        <div className="px-6 py-4 border-b flex items-center justify-between">
          <h2 className="text-sm font-semibold text-gray-700">Recent Reservations</h2>
          <span className="text-xs text-gray-400">last 10 · auto-refresh 3s</span>
        </div>
        {reservations.length === 0 ? (
          <p className="px-6 py-10 text-center text-sm text-gray-400">
            No reservations processed yet
          </p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b bg-gray-50 text-left">
                <th className="px-6 py-3 font-medium text-gray-500">Reservation ID</th>
                <th className="px-6 py-3 font-medium text-gray-500">Order ID</th>
                <th className="px-6 py-3 font-medium text-gray-500">Products</th>
                <th className="px-6 py-3 font-medium text-gray-500 text-right">Stock After</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-100">
              {[...reservations].reverse().slice(0, 10).map((r) => {
                const productIds = Object.keys(r.stock)
                return (
                  <tr key={r.reservationId} className="hover:bg-gray-50 transition-colors">
                    <td className="px-6 py-3 font-mono text-xs text-gray-400">
                      {r.reservationId.slice(0, 8)}…
                    </td>
                    <td className="px-6 py-3 font-mono text-gray-900">#{r.orderId}</td>
                    <td className="px-6 py-3 text-gray-600">
                      {productIds.length} product{productIds.length !== 1 ? "s" : ""}
                    </td>
                    <td className="px-6 py-3 text-right text-gray-500 text-xs font-mono">
                      {productIds.map((pid) => `#${pid}: ${r.stock[pid]}`).join(", ")}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        )}
      </div>
    </div>
  )
}
