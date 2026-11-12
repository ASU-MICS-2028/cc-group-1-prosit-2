import { useTranslation } from "react-i18next"
import { Link } from "react-router-dom"
import { PhotoThumb } from "@/components/capture/PhotoThumb"
import { Clock, TriangleAlert } from "@/components/icons"
import { EmptyState } from "@/components/PageHeader"
import type { TestResult } from "@/db/types"
import { formatDateTime } from "@/lib/dates"
import { usePhotosFor } from "@/lib/media"
import { cn } from "@/lib/utils"
import { useResults } from "./tests"

/**
 * A patient's tests on their record: those sent out and still waiting (with "Results are back"),
 * and those with an answer, with the photos of the slip or the film.
 */
export function ResultsSection({ patientId }: { patientId: string }) {
  const { t } = useTranslation()
  const results = useResults(patientId)
  return (
    <section id="results" className="scroll-mt-6 space-y-3">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-heading">
          {t("results.title")}
        </h2>
        <Link
          to={`/patients/${patientId}/results/new`}
          className="inline-flex min-h-10 items-center rounded-full bg-secondary px-4 text-sm font-semibold text-secondary-foreground"
        >
          {t("results.add")}
        </Link>
      </div>
      {results?.length === 0 ? (
        <EmptyState>{t("results.empty")}</EmptyState>
      ) : (
        <ul className="space-y-2">
          {results?.map((r) => (
            <ResultCard key={r.id} r={r} />
          ))}
        </ul>
      )}
    </section>
  )
}

function ResultCard({ r }: { r: TestResult }) {
  const { t } = useTranslation()
  const photos = usePhotosFor(r.id, "result-photo")
  const waiting = r.status === "requested"
  return (
    <li className="rounded-xl bg-card p-4 text-sm shadow-card">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="font-semibold text-heading">{r.test}</p>
          <p className="text-muted-foreground">
            {t(`results.category.${r.category}`)}
            {r.facility ? ` · ${r.facility}` : ""}
          </p>
        </div>
        <span
          className={cn(
            "shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold",
            waiting
              ? "bg-warning/10 text-warning"
              : r.status === "cancelled"
                ? "bg-muted text-muted-foreground"
                : "bg-secondary text-secondary-foreground"
          )}
        >
          {t(`results.status.${r.status}`)}
        </span>
      </div>
      <p className="mt-1 text-muted-foreground">
        {waiting
          ? t("results.sentOn", { date: formatDateTime(r.requestedAt) })
          : r.resultedAt
            ? formatDateTime(r.resultedAt)
            : null}
      </p>
      {r.result ? (
        <p className="mt-2 whitespace-pre-line text-foreground">{r.result}</p>
      ) : null}
      {r.flag === "abnormal" ? (
        <p className="mt-2 flex items-center gap-1.5 font-semibold text-destructive">
          <TriangleAlert className="size-4" aria-hidden />
          {t("results.flag.abnormal")}
        </p>
      ) : r.flag === "normal" ? (
        <p className="mt-2 text-muted-foreground">{t("results.flag.normal")}</p>
      ) : null}
      {photos?.length ? (
        <div className="mt-2 flex gap-2">
          {photos.map((m, i) => (
            <PhotoThumb key={m.id} media={m} n={i + 1} className="size-20" />
          ))}
        </div>
      ) : null}
      {r.notes ? <p className="mt-2 text-muted-foreground">{r.notes}</p> : null}
      {waiting ? (
        <div className="mt-3 flex items-center gap-2">
          <Clock className="size-4 text-warning" aria-hidden />
          <Link
            to={`/patients/${r.patientId}/results/${r.id}`}
            className="inline-flex h-11 flex-1 items-center justify-center rounded-lg bg-primary px-3 text-sm font-semibold text-primary-foreground"
          >
            {t("results.backNow")}
          </Link>
        </div>
      ) : null}
    </li>
  )
}
