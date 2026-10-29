import { useTranslation } from "react-i18next"

/** Figma progress bars over a multi-step form: done and current steps blue, the rest light blue. */
export function Stepper({ step, total }: { step: number; total: number }) {
  const { t } = useTranslation()
  return (
    <div className="mb-8">
      <p className="sr-only">{t("common.stepOf", { step: step + 1, total })}</p>
      <ol aria-hidden className="flex gap-4">
        {Array.from({ length: total }, (_, i) => (
          <li
            key={i}
            className={
              "h-1 flex-1 rounded-full " +
              (i <= step ? "bg-primary" : "bg-primary/25")
            }
          />
        ))}
      </ol>
    </div>
  )
}
