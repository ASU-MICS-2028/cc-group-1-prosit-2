/**
 * Health education the CHV can read out, play, or show a patient. Written in plain English from
 * WHO and Ghana Health Service guidance for primary care; native speakers should translate and
 * record each topic before it is used in Twi, Ewe, Hausa or Dagbani (until then the app reads the
 * English text aloud with the phone's own voice).
 *
 * A topic can also carry a recorded audio or video file (put it in public/education/ and name it
 * here), for example a GHS clip in a local language. The play button then plays that file instead
 * of reading the text.
 */
export interface Topic {
  id: string
  title: string
  /** one sentence under the title */
  summary: string
  /** the key points, read out in this order */
  points: string[]
  /** when to go to a health facility now */
  danger?: string[]
  audio?: string
  video?: string
  /** who the guidance comes from */
  source: string
}

export const TOPICS: readonly Topic[] = [
  {
    id: "malaria",
    title: "Malaria: prevent it, test early",
    summary:
      "Malaria spreads through mosquito bites at night. It can be prevented, and it can be cured when treated early.",
    points: [
      "Sleep under a treated mosquito net every night, all year. Children under five and pregnant women first.",
      "Clear standing water, empty old tins and cover water pots, so mosquitoes cannot breed near the house.",
      "Any fever can be malaria. Get a malaria test within one day, at the CHPS compound or from your health volunteer.",
      "If the test is positive, take all the medicine for all the days, even when you feel better.",
      "Pregnant women should take the malaria prevention medicine given at antenatal visits.",
    ],
    danger: [
      "Very high fever, fits, or being very sleepy and hard to wake",
      "Not able to drink or breastfeed, or vomiting everything",
      "Yellow eyes or very dark urine",
    ],
    source: "WHO and Ghana National Malaria Elimination Programme",
  },
  {
    id: "blood-pressure",
    title: "High blood pressure",
    summary:
      "High blood pressure usually has no signs, but it can cause a stroke or heart failure. Checking it is the only way to know.",
    points: [
      "Have your blood pressure checked at least once a year, and at every visit if it was high before.",
      "Eat less salt: no extra salt at the table, fewer stock cubes and salted fish.",
      "Eat more vegetables and fruit, walk or work actively every day, and do not smoke.",
      "Drink little or no alcohol.",
      "If you have medicine for blood pressure, take it every day, even when you feel well. Do not stop without asking the nurse.",
    ],
    danger: [
      "Very bad headache, or blurred sight",
      "Chest pain or difficult breathing",
      "Sudden weakness on one side of the body, or trouble speaking",
    ],
    source: "WHO HEARTS technical package",
  },
  {
    id: "antenatal",
    title: "Pregnancy: antenatal care",
    summary:
      "Regular check-ups keep mother and baby safe. Start early and go at least eight times.",
    points: [
      "Go for your first antenatal visit in the first three months of pregnancy.",
      "Have at least eight antenatal contacts before the baby is born.",
      "Take the iron and folic acid tablets every day, and the malaria prevention medicine at your visits.",
      "Sleep under a treated mosquito net.",
      "Plan to give birth at a health facility with a midwife, and plan how you will get there.",
    ],
    danger: [
      "Bleeding from the vagina",
      "Very bad headache, blurred sight, or fits",
      "Swelling of the face and hands",
      "Fever, or the baby has stopped moving",
      "Water breaks before labour",
    ],
    source: "WHO antenatal care recommendations, 2016",
  },
  {
    id: "diarrhoea",
    title: "Diarrhoea in children: ORS and zinc",
    summary:
      "Diarrhoea takes water out of the body. Replacing it with ORS saves children's lives.",
    points: [
      "Give ORS after every loose stool: mix one packet in clean water exactly as written on the packet.",
      "Give zinc tablets every day for 10 to 14 days, even after the diarrhoea stops.",
      "Keep breastfeeding, and keep giving food.",
      "Wash hands with soap after cleaning the child and before preparing food.",
    ],
    danger: [
      "The child cannot drink or breastfeed",
      "Very sleepy or hard to wake",
      "Blood in the stool",
      "Sunken eyes, or skin that goes back slowly when pinched",
    ],
    source: "WHO and UNICEF",
  },
  {
    id: "handwashing",
    title: "Handwashing with soap",
    summary:
      "Washing hands with soap and running water stops diarrhoea, cholera and chest infections from spreading.",
    points: [
      "Use soap and running water, for example from a Veronica bucket.",
      "Rub all parts of the hands for about 20 seconds, then rinse and air-dry.",
      "Wash after using the toilet and after cleaning a child.",
      "Wash before preparing food, before eating, and before feeding a child.",
    ],
    source: "Ghana Health Service and WHO",
  },
  {
    id: "breastfeeding",
    title: "Breastfeeding",
    summary:
      "Breast milk is the best food for a baby. It protects against diarrhoea and chest infections.",
    points: [
      "Put the baby to the breast within one hour of birth.",
      "Give only breast milk for the first six months: no water, no other drinks or food.",
      "Breastfeed day and night, whenever the baby wants.",
      "From six months, add other foods, and keep breastfeeding until two years or more.",
    ],
    source: "WHO and UNICEF",
  },
]

/** The text read aloud for a topic: title, summary, points, then when to go now. */
export function spokenText(topic: Topic, dangerHeading: string): string {
  return [
    topic.title,
    topic.summary,
    ...topic.points,
    ...(topic.danger?.length ? [dangerHeading, ...topic.danger] : []),
  ]
    .map((line) => line.trim().replace(/[.!?]$/, ""))
    .join(". ")
}
