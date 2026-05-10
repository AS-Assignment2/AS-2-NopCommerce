import { BrowserRouter, Link, Navigate, Route, Routes, useLocation } from "react-router-dom"
import { ErpPage } from "./pages/ErpPage"
import { WmsPage } from "./pages/WmsPage"

function Nav() {
  const { pathname } = useLocation()
  const link = (to: string, label: string) => (
    <Link
      to={to}
      className={`px-3 py-1.5 rounded-md text-sm font-medium transition-colors ${
        pathname === to
          ? "bg-gray-900 text-white"
          : "text-gray-500 hover:text-gray-900 hover:bg-gray-100"
      }`}
    >
      {label}
    </Link>
  )
  return (
    <nav className="border-b bg-white sticky top-0 z-10">
      <div className="mx-auto max-w-4xl flex h-14 items-center gap-6 px-6">
        <span className="font-semibold text-gray-900 tracking-tight">VerdeMart</span>
        <div className="flex gap-1">
          {link("/erp", "ERP Stub")}
          {link("/wms", "WMS Stub")}
        </div>
      </div>
    </nav>
  )
}

export default function App() {
  return (
    <BrowserRouter>
      <div className="min-h-screen bg-gray-50">
        <Nav />
        <main className="mx-auto max-w-4xl px-6 py-8">
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
