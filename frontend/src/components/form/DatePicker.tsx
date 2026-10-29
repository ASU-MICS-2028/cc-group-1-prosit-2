import { Popover } from "@base-ui/react/popover"
import { useEffect, useRef, useState } from "react"
import { useTranslation } from "react-i18next"
import { CalendarDays, ChevronRight } from "@/components/icons"
import { cn } from "@/lib/utils"

const pad = (n: number) => String(n).padStart(2, "0")
const dayKey = (d: Date) =>
  `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`
const sameDay = (a: Date, b: Date) => dayKey(a) === dayKey(b)
const startOfDay = (d: Date) =>
  new Date(d.getFullYear(), d.getMonth(), d.getDate())

/** "YYYY-MM-DD" or "YYYY-MM-DDTHH:mm" in local time, the same strings the browser's inputs use. */
function parse(value: string | undefined): Date | null {
  if (!value) return null
  const [date, time = "00:00"] = value.split("T")
  const [y, m, d] = date!.split("-").map(Number)
  const [h, min] = time.split(":").map(Number)
  if (!y || !m || !d) return null
  return new Date(y, m - 1, d, h ?? 0, min ?? 0)
}

/**
 * A date (and optionally time) field whose calendar matches the app: a text-field trigger, and a
 * white elevated card with the month, a Monday-first grid of days, a year list for birth dates,
 * and a time stepper. Replaces the browser's own picker, which looks different on every phone.
 * Values are the same strings as <input type="date"> / "datetime-local", so forms don't change.
 */
export function DatePicker({
  id,
  value,
  onChange,
  withTime = false,
  min,
  max,
  defaultTime = "09:00",
  startWithYear = false,
  placeholder,
  invalid,
  describedBy,
}: {
  id: string
  value: string | undefined
  onChange: (value: string) => void
  withTime?: boolean
  /** earliest and latest day that can be picked */
  min?: Date
  max?: Date
  /** the time used when a day is picked before a time */
  defaultTime?: string
  /** open on the year list, e.g. for a birth date decades back */
  startWithYear?: boolean
  placeholder?: string
  invalid?: boolean
  describedBy?: string
}) {
  const { t, i18n } = useTranslation()
  const lang = i18n.language
  const chosen = parse(value)
  const [open, setOpen] = useState(false)
  const popup = useRef<HTMLDivElement>(null)
  const [view, setView] = useState<"days" | "years">("days")
  const [month, setMonth] = useState(() => chosen ?? max ?? new Date())

  function onOpenChange(next: boolean) {
    setOpen(next)
    if (next) {
      const now = new Date()
      // A birth date with no value yet: open the year list around an adult's birth year.
      const start =
        startWithYear && !chosen
          ? new Date(now.getFullYear() - 30, 0, 1)
          : max && max < now
            ? max
            : now
      setMonth(chosen ?? start)
      setView(startWithYear && !chosen ? "years" : "days")
    }
  }

  const time = chosen
    ? `${pad(chosen.getHours())}:${pad(chosen.getMinutes())}`
    : defaultTime
  function emit(day: Date, hhmm = time) {
    onChange(withTime ? `${dayKey(day)}T${hhmm}` : dayKey(day))
  }

  const label = chosen
    ? new Intl.DateTimeFormat(lang, {
        dateStyle: "medium",
        ...(withTime ? { timeStyle: "short" } : {}),
      }).format(chosen)
    : null

  return (
    <Popover.Root open={open} onOpenChange={onOpenChange}>
      <Popover.Trigger
        id={id}
        aria-invalid={invalid || undefined}
        aria-describedby={describedBy}
        className={cn(
          "flex h-14 w-full items-center gap-2 rounded-sm border border-input bg-transparent px-4 text-left text-base text-foreground outline-none",
          "focus-visible:border-2 focus-visible:border-primary focus-visible:px-[15px]",
          "data-[popup-open]:border-2 data-[popup-open]:border-primary data-[popup-open]:px-[15px]",
          invalid && "border-2 border-destructive px-[15px]"
        )}
      >
        <span className="flex-1 truncate">
          {label ?? (
            <span className="text-muted-foreground">
              {placeholder ?? t("date.choose")}
            </span>
          )}
        </span>
        <CalendarDays className="size-6 text-primary" aria-hidden />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Positioner sideOffset={6} align="start" className="z-50">
          <Popover.Popup
            aria-label={t("date.choose")}
            // focus the chosen day / year (not the first button), which also scrolls it into view
            initialFocus={() =>
              popup.current?.querySelector<HTMLElement>("[data-start]") ?? true
            }
            ref={popup}
            className={cn(
              "w-[min(21.5rem,calc(100vw-2.5rem))] rounded-xl bg-card p-4 shadow-card ring-1 ring-black/5 outline-none",
              "origin-[var(--transform-origin)] transition-[transform,opacity] duration-150 data-[ending-style]:scale-95 data-[ending-style]:opacity-0 data-[starting-style]:scale-95 data-[starting-style]:opacity-0"
            )}
          >
            {view === "years" ? (
              <Years
                month={month}
                chosen={chosen}
                min={min}
                max={max}
                onPick={(y) => {
                  setMonth(new Date(y, month.getMonth(), 1))
                  setView("days")
                }}
              />
            ) : (
              <Days
                month={month}
                setMonth={setMonth}
                chosen={chosen}
                min={min}
                max={max}
                onYears={() => setView("years")}
                onPick={(day) => {
                  emit(day)
                  if (!withTime) setOpen(false)
                }}
              />
            )}
            {withTime && view === "days" ? (
              <div className="mt-3 flex items-center gap-3 rounded-xl bg-secondary/60 p-2">
                <span className="pl-2 text-sm font-medium text-muted-foreground">
                  {t("date.time")}
                </span>
                <TimeStepper
                  value={time}
                  onChange={(hhmm) => emit(chosen ?? month, hhmm)}
                />
              </div>
            ) : null}
            {withTime ? (
              <div className="mt-3 flex justify-between gap-2">
                <button
                  type="button"
                  onClick={() => {
                    const now = new Date()
                    emit(now, `${pad(now.getHours())}:${pad(now.getMinutes())}`)
                    setMonth(now)
                  }}
                  className="h-11 rounded-lg px-3 text-sm font-semibold text-primary hover:bg-secondary"
                >
                  {t("date.now")}
                </button>
                <button
                  type="button"
                  onClick={() => setOpen(false)}
                  className="h-11 rounded-lg bg-primary px-5 text-sm font-semibold text-primary-foreground"
                >
                  {t("date.done")}
                </button>
              </div>
            ) : null}
          </Popover.Popup>
        </Popover.Positioner>
      </Popover.Portal>
    </Popover.Root>
  )
}

function Days({
  month,
  setMonth,
  chosen,
  min,
  max,
  onYears,
  onPick,
}: {
  month: Date
  setMonth: (d: Date) => void
  chosen: Date | null
  min?: Date
  max?: Date
  onYears: () => void
  onPick: (day: Date) => void
}) {
  const { t, i18n } = useTranslation()
  const lang = i18n.language
  const grid = useRef<HTMLDivElement>(null)
  const today = new Date()
  const first = new Date(month.getFullYear(), month.getMonth(), 1)
  const days = new Date(month.getFullYear(), month.getMonth() + 1, 0).getDate()
  const lead = (first.getDay() + 6) % 7 // Monday first, as on Ghanaian calendars
  const outOfRange = (d: Date) =>
    (min && d < startOfDay(min)) || (max && d > startOfDay(max))
  const shift = (n: number) =>
    setMonth(new Date(month.getFullYear(), month.getMonth() + n, 1))
  const canBack = !min || first > startOfDay(min)
  const canForward =
    !max || new Date(month.getFullYear(), month.getMonth() + 1, 1) <= max

  // The tab stop in the grid: the chosen day, else today, else the 1st.
  const focusDay =
    chosen &&
    sameDay(new Date(chosen.getFullYear(), chosen.getMonth(), 1), first)
      ? chosen.getDate()
      : sameDay(new Date(today.getFullYear(), today.getMonth(), 1), first)
        ? today.getDate()
        : 1

  function onKeyDown(e: React.KeyboardEvent<HTMLButtonElement>, day: number) {
    const step = { ArrowLeft: -1, ArrowRight: 1, ArrowUp: -7, ArrowDown: 7 }[
      e.key
    ]
    if (step === undefined) return
    e.preventDefault()
    const next = day + step
    if (next < 1 || next > days) {
      shift(next < 1 ? -1 : 1)
      return
    }
    grid.current
      ?.querySelector<HTMLButtonElement>(`[data-day="${next}"]`)
      ?.focus()
  }

  const weekdays = Array.from({ length: 7 }, (_, i) =>
    new Intl.DateTimeFormat(lang, { weekday: "narrow" }).format(
      new Date(2024, 0, 1 + i) // 1 Jan 2024 was a Monday
    )
  )
  const arrow =
    "grid size-11 place-items-center rounded-full text-heading hover:bg-secondary disabled:opacity-30 disabled:hover:bg-transparent"

  return (
    <>
      <div className="mb-2 flex items-center gap-1">
        <button
          type="button"
          onClick={onYears}
          aria-label={t("date.chooseYear")}
          className="flex h-11 flex-1 items-center gap-1 rounded-lg px-2 text-left text-base font-semibold text-heading hover:bg-secondary"
        >
          {new Intl.DateTimeFormat(lang, {
            month: "long",
            year: "numeric",
          }).format(month)}
          <svg
            viewBox="0 0 24 24"
            className="size-5 text-muted-foreground"
            fill="currentColor"
            aria-hidden
          >
            <path d="M7 10l5 5 5-5z" />
          </svg>
        </button>
        <button
          type="button"
          onClick={() => shift(-1)}
          disabled={!canBack}
          aria-label={t("date.prevMonth")}
          className={arrow}
        >
          <ChevronRight className="size-6 rotate-180" aria-hidden />
        </button>
        <button
          type="button"
          onClick={() => shift(1)}
          disabled={!canForward}
          aria-label={t("date.nextMonth")}
          className={arrow}
        >
          <ChevronRight className="size-6" aria-hidden />
        </button>
      </div>
      <div className="grid grid-cols-7 text-center text-xs font-medium text-muted-foreground">
        {weekdays.map((d, i) => (
          <span key={i} aria-hidden className="py-1">
            {d}
          </span>
        ))}
      </div>
      <div ref={grid} className="grid grid-cols-7 gap-y-1">
        {Array.from({ length: lead }, (_, i) => (
          <span key={`lead${i}`} />
        ))}
        {Array.from({ length: days }, (_, i) => {
          const day = new Date(month.getFullYear(), month.getMonth(), i + 1)
          const selected = chosen ? sameDay(chosen, day) : false
          const isToday = sameDay(today, day)
          return (
            <button
              key={i}
              type="button"
              data-day={i + 1}
              data-start={i + 1 === focusDay || undefined}
              tabIndex={i + 1 === focusDay ? 0 : -1}
              disabled={outOfRange(day)}
              aria-pressed={selected}
              aria-current={isToday ? "date" : undefined}
              aria-label={new Intl.DateTimeFormat(lang, {
                dateStyle: "full",
              }).format(day)}
              onClick={() => onPick(day)}
              onKeyDown={(e) => onKeyDown(e, i + 1)}
              className={cn(
                "mx-auto grid size-10 place-items-center rounded-full text-sm tabular-nums outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
                selected
                  ? "bg-primary font-semibold text-primary-foreground"
                  : isToday
                    ? "font-semibold text-primary ring-1 ring-primary/40 ring-inset hover:bg-secondary"
                    : "text-foreground hover:bg-secondary",
                "disabled:text-muted-foreground/40 disabled:hover:bg-transparent"
              )}
            >
              {i + 1}
            </button>
          )
        })}
      </div>
    </>
  )
}

function Years({
  month,
  chosen,
  min,
  max,
  onPick,
}: {
  month: Date
  chosen: Date | null
  min?: Date
  max?: Date
  onPick: (year: number) => void
}) {
  const { t } = useTranslation()
  const list = useRef<HTMLDivElement>(null)
  const now = new Date().getFullYear()
  const from = min?.getFullYear() ?? now - 120
  const to = max?.getFullYear() ?? now + 5
  const current = month.getFullYear()

  // Start with the current year in the middle of the list rather than at the top. Scrolls the
  // list only (scrollIntoView would also scroll the page), once the popup has its size.
  useEffect(() => {
    const frame = requestAnimationFrame(() => {
      const el = list.current
      const year = el?.querySelector<HTMLElement>(`[data-year="${current}"]`)
      if (el && year)
        el.scrollTop = year.offsetTop - el.offsetTop - el.clientHeight / 2
    })
    return () => cancelAnimationFrame(frame)
  }, [current])

  return (
    <>
      <p className="mb-2 px-2 text-base font-semibold text-heading">
        {t("date.chooseYear")}
      </p>
      <div
        ref={list}
        className="grid max-h-72 grid-cols-4 gap-1 overflow-y-auto [scrollbar-width:thin]"
      >
        {Array.from({ length: to - from + 1 }, (_, i) => to - i).map((y) => (
          <button
            key={y}
            type="button"
            data-year={y}
            data-start={y === current || undefined}
            onClick={() => onPick(y)}
            aria-pressed={chosen?.getFullYear() === y}
            className={cn(
              "h-11 rounded-full text-sm tabular-nums outline-none focus-visible:ring-3 focus-visible:ring-ring/50",
              chosen?.getFullYear() === y
                ? "bg-primary font-semibold text-primary-foreground"
                : y === current
                  ? "font-semibold text-primary ring-1 ring-primary/40 ring-inset"
                  : "text-foreground hover:bg-secondary"
            )}
          >
            {y}
          </button>
        ))}
      </div>
    </>
  )
}

/** Hour and minute, each with up/down buttons; minutes move in fives. */
function TimeStepper({
  value,
  onChange,
}: {
  value: string
  onChange: (hhmm: string) => void
}) {
  const { t } = useTranslation()
  const [h, m] = value.split(":").map(Number) as [number, number]
  const set = (hh: number, mm: number) =>
    onChange(`${pad((hh + 24) % 24)}:${pad((mm + 60) % 60)}`)
  const part = (
    label: string,
    shown: number,
    up: () => void,
    down: () => void
  ) => (
    <div
      role="spinbutton"
      tabIndex={0}
      aria-label={label}
      aria-valuenow={shown}
      onKeyDown={(e) => {
        const move = { ArrowUp: up, ArrowDown: down }[e.key]
        if (!move) return
        e.preventDefault()
        move()
      }}
      className="flex items-center rounded-lg bg-card shadow-card outline-none focus-visible:ring-3 focus-visible:ring-ring/50"
    >
      <button
        type="button"
        tabIndex={-1}
        aria-hidden
        onClick={down}
        className="grid h-11 w-8 place-items-center text-muted-foreground"
      >
        <ChevronRight className="size-5 rotate-90" />
      </button>
      <span className="w-7 text-center text-lg font-semibold text-heading tabular-nums">
        {pad(shown)}
      </span>
      <button
        type="button"
        tabIndex={-1}
        aria-hidden
        onClick={up}
        className="grid h-11 w-8 place-items-center text-muted-foreground"
      >
        <ChevronRight className="size-5 -rotate-90" />
      </button>
    </div>
  )
  return (
    <div className="ml-auto flex items-center gap-1.5">
      {part(
        t("date.hour"),
        h,
        () => set(h + 1, m),
        () => set(h - 1, m)
      )}
      <span className="font-semibold text-heading">:</span>
      {part(
        t("date.minute"),
        m,
        () => set(h, Math.floor(m / 5) * 5 + 5),
        () => set(h, Math.ceil(m / 5) * 5 - 5)
      )}
    </div>
  )
}
