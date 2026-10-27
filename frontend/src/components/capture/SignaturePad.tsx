import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"

/** White, like the paper form, in light and dark mode, so the saved PNG reads anywhere. */
function paper(ctx: CanvasRenderingContext2D, el: HTMLCanvasElement) {
  ctx.save()
  ctx.setTransform(1, 0, 0, 1, 0, 0)
  ctx.fillStyle = "#ffffff"
  ctx.fillRect(0, 0, el.width, el.height)
  ctx.restore()
}

/**
 * A box to sign in with a finger (or mouse), with a baseline like a paper form and Clear.
 * Each finished stroke saves the drawing as a small PNG (~5-15 KB). Pointer events, so touch,
 * pen and mouse all work; touch-action none so drawing doesn't scroll the page.
 */
export function SignaturePad({
  label,
  onChange,
}: {
  label: string
  onChange: (png: Blob | undefined) => void
}) {
  const { t } = useTranslation()
  const canvas = useRef<HTMLCanvasElement>(null)
  const last = useRef<{ x: number; y: number } | null>(null)
  const [signed, setSigned] = useState(false)

  // Draw at the screen's real pixel density so the signature isn't blurry.
  useEffect(() => {
    const el = canvas.current
    const ctx = el?.getContext("2d")
    if (!el || !ctx) return
    const ratio = window.devicePixelRatio || 1
    el.width = el.offsetWidth * ratio
    el.height = el.offsetHeight * ratio
    ctx.scale(ratio, ratio)
    ctx.lineWidth = 2.5
    ctx.lineCap = "round"
    ctx.lineJoin = "round"
    ctx.strokeStyle = "#242731"
    paper(ctx, el)
  }, [])

  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const box = e.currentTarget.getBoundingClientRect()
    return { x: e.clientX - box.left, y: e.clientY - box.top }
  }

  function down(e: React.PointerEvent<HTMLCanvasElement>) {
    e.currentTarget.setPointerCapture(e.pointerId)
    last.current = point(e)
  }

  function move(e: React.PointerEvent<HTMLCanvasElement>) {
    const ctx = canvas.current?.getContext("2d")
    if (!last.current || !ctx) return
    const p = point(e)
    ctx.beginPath()
    ctx.moveTo(last.current.x, last.current.y)
    ctx.lineTo(p.x, p.y)
    ctx.stroke()
    last.current = p
    setSigned(true)
  }

  function up() {
    if (!last.current) return
    last.current = null
    canvas.current?.toBlob((png) => png && onChange(png), "image/png")
  }

  function clear() {
    const el = canvas.current
    const ctx = el?.getContext("2d")
    if (el && ctx) paper(ctx, el)
    setSigned(false)
    onChange(undefined)
  }

  return (
    <div className="rounded-xl bg-card p-4 shadow-card">
      <div className="relative">
        <canvas
          ref={canvas}
          role="img"
          aria-label={label}
          onPointerDown={down}
          onPointerMove={move}
          onPointerUp={up}
          onPointerCancel={up}
          className="h-40 w-full touch-none rounded-lg bg-white"
        />
        {/* the line to sign on, as on the paper form */}
        <div
          aria-hidden
          className="pointer-events-none absolute inset-x-4 bottom-8 border-b-2 border-dashed border-[#5c616c]/40"
        />
        {!signed ? (
          <span
            aria-hidden
            className="pointer-events-none absolute bottom-10 left-4 text-sm text-[#5c616c]"
          >
            {t("capture.signHere")}
          </span>
        ) : null}
      </div>
      <div className="mt-2 flex items-center justify-between gap-3">
        <p className="text-xs text-muted-foreground">{t("capture.signHint")}</p>
        <button
          type="button"
          onClick={clear}
          disabled={!signed}
          className="h-11 shrink-0 rounded-lg px-3 text-sm font-semibold text-primary hover:bg-secondary disabled:opacity-40"
        >
          {t("capture.clear")}
        </button>
      </div>
    </div>
  )
}
