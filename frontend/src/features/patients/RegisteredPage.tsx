import { HeartPulse } from "lucide-react"
import { useTranslation } from "react-i18next"
import { Link, useParams } from "react-router-dom"
import { buttonVariants } from "@/components/ui/button"
import { cn } from "@/lib/utils"
import { fullName, usePatient } from "./usePatients"

/**
 * After registering a patient (Figma "Felicidades!"): confirmation, then the likely next step,
 * recording vitals, so the CHV doesn't have to find the patient again.
 */
export function Component() {
  const { t } = useTranslation()
  const { id = "" } = useParams()
  const patient = usePatient(id)

  return (
    <div className="mx-auto flex min-h-[calc(100svh-6rem)] max-w-md flex-col items-center gap-4 text-center">
      <h1 className="pt-4 text-[2.5rem] leading-tight font-bold text-primary min-[400px]:text-[3rem]">
        {t("registered.title")}
      </h1>
      <img
        src="/illustrations/registered.svg"
        alt=""
        width={325}
        height={271}
        className="h-auto w-full max-w-80"
      />
      <p className="text-xl font-semibold text-foreground">
        {patient
          ? t("registered.saved", { name: fullName(patient) })
          : t("registered.savedPlain")}
      </p>
      <p className="text-sm text-muted-foreground">{t("registered.text")}</p>
      <div className="mt-auto w-full space-y-3 pt-4">
        <Link
          to={`/patients/${id}/vitals/new`}
          replace
          className={cn(buttonVariants({ size: "xl" }), "w-full")}
        >
          <HeartPulse aria-hidden />
          {t("registered.recordVitals")}
        </Link>
        <Link
          to={`/patients/${id}`}
          replace
          className={cn(
            buttonVariants({ size: "xl", variant: "secondary" }),
            "w-full"
          )}
        >
          {t("registered.done")}
        </Link>
      </div>
    </div>
  )
}
