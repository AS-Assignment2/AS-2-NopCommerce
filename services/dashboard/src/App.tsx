import { BrowserRouter, Link, Navigate, Route, Routes, useLocation } from "react-router-dom"
import { useEffect, useState } from "react"
import { Activity, Package, Warehouse, Radio } from "lucide-react"
import { ErpPage } from "./pages/ErpPage"
import { WmsPage } from "./pages/WmsPage"

const ERP_URL = (import.meta.env.VITE_ERP_URL as string | undefined) ?? "http://localhost:8001"
const WMS_URL = (import.meta.env.VITE_WMS_URL as string | undefined) ?? "http://localhost:8002"

function PulseDot({ state }: { state: "online" | "offline" | "checking" }) {
  if (state === "checking")
    return <span className="h-2 w-2 rounded-full bg-slate-500" />
  if (state === "offline")
    return <span className="h-2 w-2 rounded-full bg-red-500" />
  return (
    <span className="relative flex h-2 w-2">
      <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-60" />
      <span className="relative inline-flex h-2 w-2 rounded-full bg-emerald-500" />
    </span>
  )
}

function Sidebar() {
  const { pathname } = useLocation()
  const [erp, setErp] = useState<"checking" | "online" | "offline">("checking")
  const [wms, setWms] = useState<"checking" | "online" | "offline">("checking")

  useEffect(() => {
    const check = async () => {
      try { await fetch(`${ERP_URL}/health`); setErp("online") } catch { setErp("offline") }
      try { await fetch(`${WMS_URL}/health`); setWms("online") } catch { setWms("offline") }
    }
    void check()
    const id = setInterval(() => void check(), 5000)
    return () => clearInterval(id)
  }, [])

  const navItem = (
    to: string,
    label: string,
    sub: string,
    icon: React.ReactNode,
    state: "checking" | "online" | "offline",
  ) => (
    <Link
      to={to}
      className={`group flex items-center gap-3 rounded-lg px-3 py-3 text-sm transition-all ${
        pathname === to
          ? "bg-white/10 text-white"
          : "text-slate-400 hover:bg-white/5 hover:text-slate-200"
      }`}
    >
      <span className={`flex h-8 w-8 items-center justify-center rounded-md ${pathname === to ? "bg-white/10" : "bg-white/5 group-hover:bg-white/10"}`}>
        {icon}
      </span>
      <div className="flex-1 min-w-0">
        <p className="font-medium leading-none">{label}</p>
        <p className="text-xs text-slate-500 mt-0.5">{sub}</p>
      </div>
      <PulseDot state={state} />
    </Link>
  )

  return (
    <div className="fixed inset-y-0 left-0 flex w-60 flex-col bg-slate-900 border-r border-slate-800">
      {/* Logo */}
      <div className="flex h-16 shrink-0 items-center gap-3 border-b border-slate-800 px-5">
        <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-emerald-500 shadow-lg shadow-emerald-500/30">
          <Activity size={18} className="text-white" />
        </div>
        <div>
          <p className="font-bold text-white text-sm leading-tight">VerdeMart</p>
          <p className="text-xs text-slate-500">Integration Monitor</p>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 overflow-y-auto px-3 py-4 space-y-0.5">
        <p className="px-3 pb-2 text-[10px] font-semibold uppercase tracking-widest text-slate-600">
          Stub Services
        </p>
        {navItem("/erp", "ERP Stub", "port 8001", <Package size={15} />, erp)}
        {navItem("/wms", "WMS Stub", "port 8002", <Warehouse size={15} />, wms)}
      </nav>

      {/* Footer */}
      <div className="shrink-0 border-t border-slate-800 px-5 py-4">
        <div className="flex items-center gap-2 text-xs text-slate-600">
          <Radio size={11} className="text-slate-500" />
          <span>Auto-refresh every 3s</span>
        </div>
        <p className="mt-1 text-[10px] text-slate-700">AS 2025/26 · Scenario C</p>
      </div>
    </div>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <div className="bg-slate-50 min-h-screen">
        <Sidebar />
        <main className="ml-60 min-h-screen">
          <Routes>
            <Route path="/" element={<Navigate to="/erp" replace />} />
            <Route path="/erp" element={<ErpPage />} />
            <Route path="/wms" element={<WmsPage />} />
          </Routes>
        </main>
      </div>
    </BrowserRouter>
  )
}
