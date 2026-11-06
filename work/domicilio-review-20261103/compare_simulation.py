import csv
import json
import sys
from collections import Counter
from pathlib import Path

source = Path(r"C:\Users\giacomo_pavone\Desktop\Lavorazione_Automatica_Domicilio_20262027_LAVORAZIONE_20261103_093012.csv")
simulation = Path(sys.argv[1])

def read(path):
    with path.open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream, delimiter=";"))

old_rows = read(source)
new_rows = read(simulation)
old = {r["NUM_ISTANZA"]: r for r in old_rows if r["TIPO_RIGA"] == "ISTANZA" and r["AZIONE_ISTANZA"] == "IGNORATA"}
new_list = [r for r in new_rows if r["TIPO_RIGA"] == "ISTANZA"]
new = {r["NUM_ISTANZA"]: r for r in new_list}
assert len(new) == len(new_list), "Riepiloghi duplicati"
assert all(r["MODALITA"] == "SIMULAZIONE" and r["AZIONE_ISTANZA"] in {"IDONEA", "IDONEA_CON_CORREZIONI", "IGNORATA"} for r in new_rows)
assert all(len(r) == len(new_rows[0]) and None not in r for r in new_rows), "Riga CSV non valida"
for request in new_list:
    operations = [r for r in new_rows if r["NUM_ISTANZA"] == request["NUM_ISTANZA"] and r["TIPO_RIGA"] != "ISTANZA"]
    assert len(operations) == int(request["NUM_OPERAZIONI"])
    assert all(r["AZIONE_ISTANZA"] == request["AZIONE_ISTANZA"] for r in operations)
    if request["AZIONE_ISTANZA"] == "IDONEA_CON_CORREZIONI":
        assert all(request[k] for k in ["CORREZIONI_PROPOSTE", "VALORI_ORIGINALI", "VALORI_DOCUMENTATI", "FONTI_CORREZIONI"])
        assert request["ESITO_DOPO_CORREZIONI"] == "ACCETTABILE_DIRETTAMENTE"
    if request["PRESA_CARICO"].upper() in {"SI", "SÌ", "1", "TRUE"}:
        assert request["AZIONE_ISTANZA"] == "IGNORATA"

columns = ["NUM_ISTANZA", "COD_FISCALE", "STUDENTE", "ESITO_PRECEDENTE", "MOTIVO_PRECEDENTE", "ESITO_SIMULAZIONE", "COSA_FARE", "MOTIVO_ATTUALE", "CODICI_ANOMALIA_ATTUALI", "CORREZIONI_PROPOSTE", "VALORI_ORIGINALI", "VALORI_DOCUMENTATI", "FONTI_CORREZIONI", "ESITO_DOPO_CORREZIONI", "MOTIVO_DOPO_CORREZIONI"]
destination = simulation.parent / "Confronto_non_lavorate_20261103.csv"
with destination.open("w", encoding="utf-8-sig", newline="") as stream:
    writer = csv.DictWriter(stream, fieldnames=columns, delimiter=";")
    writer.writeheader()
    for key, previous in sorted(old.items(), key=lambda pair: int(pair[0])):
        current = new.get(key, {})
        outcome = current.get("AZIONE_ISTANZA", "NON_PRESENTE_NELLA_SIMULAZIONE")
        action = {
            "IDONEA": "Idonea all'accoglimento automatico con i dati attuali; simulazione senza modifiche.",
            "IDONEA_CON_CORREZIONI": "Idonea alla correzione documentata e all'accoglimento nella stessa lavorazione; simulazione senza modifiche.",
            "IGNORATA": "Lasciare alla lavorazione manuale; consultare la motivazione attuale.",
        }.get(outcome, "Verificare lo stato attuale: istanza non inclusa tra quelle inviate nella nuova lettura.")
        row = {k: current.get(k, "") for k in columns}
        row.update(NUM_ISTANZA=key, COD_FISCALE=previous["COD_FISCALE"], STUDENTE=previous["STUDENTE"],
                   ESITO_PRECEDENTE=previous["AZIONE_ISTANZA"], MOTIVO_PRECEDENTE=previous["MOTIVO_AZIONE"],
                   ESITO_SIMULAZIONE=outcome, COSA_FARE=action, MOTIVO_ATTUALE=current.get("MOTIVO_AZIONE", ""),
                   CODICI_ANOMALIA_ATTUALI=current.get("CODICI_ANOMALIA", ""))
        writer.writerow(row)

sample_ids = ["17616", "17624", "17840", "19018", "19267", "22297", "23035", "23793"]
summary = {
    "source": str(source), "simulation": str(simulation), "comparison": str(destination),
    "requests": len(new), "rows": len(new_rows),
    "outcomes": dict(Counter(r["AZIONE_ISTANZA"] for r in new_list)),
    "original_ignored": len(old),
    "original_ignored_now": dict(Counter(new.get(key, {}).get("AZIONE_ISTANZA", "NON_PRESENTE") for key in old)),
    "corrected_ids": [r["NUM_ISTANZA"] for r in new_list if r["AZIONE_ISTANZA"] == "IDONEA_CON_CORREZIONI"],
    "proposals": sum(bool(r["CORREZIONI_PROPOSTE"]) for r in new_list),
    "examples": {key: {k: new.get(key, {}).get(k, "") for k in ["AZIONE_ISTANZA", "MOTIVO_AZIONE", "CORREZIONI_PROPOSTE", "VALORI_ORIGINALI", "VALORI_DOCUMENTATI", "FONTI_CORREZIONI"]} for key in sample_ids},
}
for key in ["17616", "17624", "22297", "23793"]:
    if key in new:
        assert new[key]["AZIONE_ISTANZA"] == "IGNORATA", f"Caso prudenziale promosso: {key}"
report = Path(__file__).with_name("simulation-comparison.json")
report.write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in summary.items() if k != "examples"}, ensure_ascii=False, indent=2))
