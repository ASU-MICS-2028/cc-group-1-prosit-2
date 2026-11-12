"""The texts patients get, in the language on their record.

English is checked; Twi, Ewe and Hausa are first drafts, like the app's own translations, and need a
native speaker before real patients get them. Dagbani has no draft yet (the app marks it "coming
soon"), so it, and any language not listed, falls back to English. Each text stays short: one SMS
is 160 characters, and every extra part costs on the sending phone's bundle.

Placeholders: {facility}, {when} (Ghana time), {code}.
"""

TEXTS = {
    "en": {
        "visit": "HealthConnect: reminder of your health visit at {facility} on {when}. If you cannot come, please tell your health volunteer.",
        "teleconsult": "HealthConnect: reminder of your phone consultation with a doctor ({facility}) on {when}. If you cannot make it, please tell your health volunteer.",
        "accepted": "HealthConnect: {facility} has accepted your referral for {when}. Please go as booked and take your health book.",
        "declined": "HealthConnect: {facility} cannot see you on {when}. Your health volunteer will contact you to book another place.",
        "prescription": "HealthConnect: your prescription {code} is ready. Show this code at any licensed pharmacy or health centre.",
    },
    "tw": {
        "visit": "HealthConnect: yɛrekae wo sɛ wowɔ apɔmuden nsrahwɛ wɔ {facility} {when}. Sɛ worentumi mma a, ka kyerɛ wo apɔmuden boafoɔ.",
        "teleconsult": "HealthConnect: yɛrekae wo sɛ wo ne dɔkota bɛkasa wɔ fon so ({facility}) {when}. Sɛ worentumi a, ka kyerɛ wo apɔmuden boafoɔ.",
        "accepted": "HealthConnect: {facility} agye wo nsoma no ato mu ama {when}. Yɛsrɛ wo kɔ sɛdeɛ yɛahyehyɛ no na fa wo apɔmuden nhoma.",
        "declined": "HealthConnect: {facility} ntumi nhwɛ wo {when}. Wo apɔmuden boafoɔ bɛfrɛ wo na wahyehyɛ baabi foforɔ.",
        "prescription": "HealthConnect: wo nnuro krataa {code} awie. Kyerɛ code yi wɔ nnuro dwam anaa apɔmuden fie biara.",
    },
    "ee": {
        "visit": "HealthConnect: ŋkuɖodzinya na wò lãmesẽ sasrãkpɔ le {facility} le {when}. Ne màte ŋu ava o la, gblɔe na wò lãmesẽ kpeɖeŋutɔ.",
        "teleconsult": "HealthConnect: ŋkuɖodzinya na wò fon dzi dzeɖoɖo kple dɔkta ({facility}) le {when}. Ne màte ŋu o la, gblɔe na wò lãmesẽ kpeɖeŋutɔ.",
        "accepted": "HealthConnect: {facility} xɔ wò ɖoɖo na {when}. Taflatse yi abe ale si míeɖoe ene eye nàtsɔ wò lãmesẽ agbalẽ.",
        "declined": "HealthConnect: {facility} mate ŋu akpɔ wò le {when} o. Wò lãmesẽ kpeɖeŋutɔ ayɔ wò be woaɖo teƒe bubu.",
        "prescription": "HealthConnect: wò atike ƒe agbalẽ {code} le klalo. Ɖe code sia fia le atikedzraƒe alo lãmesẽ ƒe xɔ ɖesiaɖe.",
    },
    "ha": {
        "visit": "HealthConnect: tunatarwa game da ziyarar lafiyarka a {facility} ranar {when}. Idan ba za ka iya zuwa ba, gaya wa mai taimaka maka a lafiya.",
        "teleconsult": "HealthConnect: tunatarwa game da tattaunawarka da likita ta waya ({facility}) ranar {when}. Idan ba za ka samu ba, gaya wa mai taimaka maka.",
        "accepted": "HealthConnect: {facility} ta karɓi turawarka ranar {when}. Don Allah je kamar yadda aka tsara, ka ɗauki littafin lafiyarka.",
        "declined": "HealthConnect: {facility} ba za ta iya ganinka ranar {when} ba. Mai taimaka maka zai kira ka don shirya wani wuri.",
        "prescription": "HealthConnect: takardar maganinka {code} ta shirya. Nuna wannan lambar a kowane kantin magani ko cibiyar lafiya.",
    },
}


def text(kind, language, **values):
    """The text of this kind in the patient's language (English when there is none for it)."""
    lang = (language or "en").lower()
    template = TEXTS.get(lang, TEXTS["en"]).get(kind) or TEXTS["en"][kind]
    return template.format(**values)
