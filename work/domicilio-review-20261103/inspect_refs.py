import json
import re
from collections import Counter
from pathlib import Path

from pypdf import PdfReader

ROOT = Path(__file__).resolve().parent
INPUT = json.loads((ROOT / "ref-groups.json").read_text(encoding="utf-8-sig"))
MANIFEST = json.loads((ROOT / "pdf-manifest.json").read_text(encoding="utf-8-sig"))
ID_RE = re.compile(r"Codice\s+Identificativo\s+del\s+contratto\s*[:\-]?\s*([A-Z0-9]{10,60})", re.I)
PROTOCOL_RE = re.compile(r"(?:protocollo(?:\s+di\s+ricezione)?\s*[:\-]?|acquisito\s+con\s+protocollo)\s*([0-9]{10,25}(?:\s*[-/]\s*[0-9]{4,8})?)", re.I)
REGISTRATION_RE = re.compile(r"registrat[oa]\s+il\s*\d{1,2}[/.\-]\d{1,2}[/.\-]\d{4}\s+al\s+n\.?\s*(\d+)\s*[-–—/]?\s*serie\s*([A-Z0-9]+)", re.I)


def compact(value):
    return re.sub(r"[^A-Z0-9]", "", value.upper())


def digits(value):
    return re.sub(r"\D", "", value)


def number(value):
    return value.lstrip("0") or "0"


def read_file(record):
    path = Path(record["Path"])
    result = {"attachment_id": record["AttachmentId"], "path": str(path), "pages": [], "errors": []}
    if record["State"] != "DOWNLOADED" or not path.exists():
        result["errors"].append("PDF_NOT_DOWNLOADED")
        return result
    try:
        reader = PdfReader(path)
        for page_number, page in enumerate(reader.pages, 1):
            raw = page.extract_text() or ""
            text = re.sub(r"\s+", " ", raw).strip()
            ids = list(dict.fromkeys(m.group(1).upper() for m in ID_RE.finditer(text)))
            protocols = []
            for match in PROTOCOL_RE.finditer(text):
                context = text[max(0, match.start()-55):min(len(text), match.end()+170)]
                following = text[match.end():match.end()+190]
                if re.search(r"richiesta\s+di\s+registrazione", following, re.I):
                    kind = "REGISTRAZIONE"
                elif re.search(r"comunicazione\s+di\s+proroga", following, re.I):
                    kind = "PROROGA"
                elif re.search(r"comunicazione\s+di\s+(?:cessione|subentro)", following, re.I):
                    kind = "SUBENTRO_CESSIONE"
                else:
                    kind = "ALTRO_O_NON_DETERMINATO"
                protocols.append({"value": match.group(1).strip(), "digits": digits(match.group(1)),
                                  "kind": kind, "context": context})
            registrations = [{"number": m.group(1), "series": m.group(2).upper(), "raw": m.group(0)}
                             for m in REGISTRATION_RE.finditer(text)]
            result["pages"].append({"page": page_number, "text_present": bool(text), "contract_ids": ids,
                                    "protocols": protocols, "registrations": registrations, "_text": text})
    except Exception as error:
        result["errors"].append(type(error).__name__)
    return result


cache = {}
results = []
for item in INPUT["items"]:
    declared, extracted = item["declared"], item["extracted"]
    attachment_ids = {int(x) for x in re.findall(r"\d+", item.get("attachment_ids", ""))}
    source_files = [record for record in MANIFEST["Files"]
                    if item["id"] in record["RequestIds"] and record["AttachmentId"] in attachment_ids]
    documents = []
    for record in source_files:
        key = record["PhysicalId"]
        if key not in cache:
            cache[key] = read_file(record)
        documents.append(cache[key])
    pages = [(document, page) for document in documents for page in document["pages"]]
    all_ids = sorted({value for _, page in pages for value in page["contract_ids"]})
    exact_pages = [(document, page) for document, page in pages if extracted.upper() in page["contract_ids"]]
    provenance = []
    result = "NON_CONFERMATO"
    detail = "L'equivalenza non e' stata verificata automaticamente sul documento."
    if not source_files or any(document["errors"] for document in documents):
        result = "DOCUMENTO_NON_DISPONIBILE_O_ERRORE_LETTURA"
    elif len(all_ids) > 1:
        result = "PIU_IDENTIFICATIVI_CONTRATTO"
        detail = "Il file contiene piu' identificativi contratto distinti: occorre segmentare e associare la ricevuta."
    elif item["subtype"] == "protocollo_numerico_vs_id_contratto":
        expected = digits(declared)
        for document, page in exact_pages:
            body_protocols = {(p["digits"], p["kind"]) for p in page["protocols"] if p["kind"] != "ALTRO_O_NON_DETERMINATO"}
            for protocol in page["protocols"]:
                value = protocol["digits"]
                matching = expected == value or len(expected) == 17 and len(value) == 23 and value.startswith(expected)
                if matching and protocol["kind"] == "REGISTRAZIONE":
                    provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                                       "page": page["page"], "kind": "PROTOCOLLO_REGISTRAZIONE_E_ID_STESSA_PAGINA",
                                       "declared_protocol": declared, "pdf_protocol": protocol["value"],
                                       "contract_id": extracted, "receipt_protocols_on_page": len(body_protocols),
                                       "context": protocol["context"]})
        if provenance and all(entry["receipt_protocols_on_page"] == 1 for entry in provenance):
            result = "EQUIVALENZA_DOCUMENTATA"
            detail = "Il protocollo dichiarato e' etichettato come richiesta di registrazione nella stessa pagina dell'identificativo univoco."
    elif item["subtype"] == "id_contratto_prefissato_3t":
        if exact_pages and compact(declared) == "3T" + compact(extracted):
            document, page = exact_pages[0]
            provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                               "page": page["page"], "kind": "IDENTIFICATIVO_COMPLETO_CON_PREFISSO",
                               "label": "Codice Identificativo del contratto", "value": extracted})
            result = "EQUIVALENZA_DOCUMENTATA"
            detail = "Il dichiarato contiene l'intero identificativo univoco con il solo prefisso 3T e punteggiatura."
    elif item["subtype"] == "numero_serie_formato_compatto":
        normalized = compact(declared)
        match = re.fullmatch(r"(?:3T(?P<first>\d+)|N?(?P<last>\d+)3T)", normalized)
        expected_number = (match.group("first") or match.group("last")) if match else normalized if re.fullmatch(r"[\d\s]+", declared) else None
        expected_series = "3T" if match else None
        for document, page in exact_pages:
            unique = {(number(reg["number"]), reg["series"]) for reg in page["registrations"]}
            for registration in page["registrations"]:
                if expected_number and number(expected_number) == number(registration["number"]) and (
                        expected_series is None or expected_series == registration["series"]):
                    provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                                       "page": page["page"], "kind": "NUMERO_SERIE_ETICHETTATI_E_ID_STESSA_PAGINA",
                                       "contract_id": extracted, "number": registration["number"],
                                       "series": registration["series"], "raw": registration["raw"],
                                       "registration_pairs_on_page": len(unique)})
        if provenance and all(entry["registration_pairs_on_page"] == 1 for entry in provenance):
            result = "EQUIVALENZA_DOCUMENTATA"
            detail = "Numero e serie del formato dichiarato coincidono con gli estremi espliciti della stessa ricevuta e dell'identificativo univoco."
    else:
        for document, page in pages:
            if page["contract_ids"] or page["registrations"]:
                provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                                   "page": page["page"], "kind": "ESTREMI_DOCUMENTALI_DA_CONFRONTARE",
                                   "contract_ids": page["contract_ids"], "registrations": page["registrations"]})
        assigned = [(document, page) for document, page in pages if re.search(
            r"Identificativo\s+assegnato\s+dal\s+richiedente\s*:\s*" + re.escape(declared) + r"(?=\s|$)",
            page["_text"], re.I)]
        if assigned:
            document, page = assigned[0]
            result = "IDENTIFICATIVO_ASSEGNATO_DAL_RICHIEDENTE"
            detail = "Il dichiarato e' presente nell'omonima etichetta della ricevuta, ma non e' un estremo ufficiale di registrazione: occorre una decisione sulla sua ammissibilita'."
            provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                               "page": page["page"], "kind": "IDENTIFICATIVO_RICHIEDENTE_ETICHETTATO",
                               "label": "Identificativo assegnato dal richiedente", "value": declared})
        elif declared.upper() == "T3":
            result = "DICHIARATO_SOLO_SERIE"
            detail = "La sola serie non identifica un contratto: sostituirla con l'identificativo sarebbe correzione dati."
        elif extracted == "2026":
            result = "EQUIVALENZA_VISIVA_TESTO_CORROTTO"
            detail = "La scansione e' leggibile visivamente; il testo incorporato omette parti delle etichette e altera l'identificativo. Nessuna promozione automatica e nessun nuovo OCR eseguito."
            document = documents[0]
            provenance.append({"attachment_id": document["attachment_id"], "path": document["path"],
                               "page": 2, "kind": "VERIFICA_VISIVA_SCANSIONE",
                               "contract_id_visual": "TJK26T009266000EE",
                               "protocol_visual": "26101518522321129 - 000001",
                               "registration_number_visual": "009266", "series_visual": "3T",
                               "render_path": str(ROOT / "ref-renders/23793-p2.png")})
        elif provenance:
            result = "DICHIARATO_DA_RICOSTRUIRE_O_DISCORDANTE"
    visual_pages = {17677: 2, 18125: 2, 17837: 1, 19680: 8, 20169: 2, 23793: 2}
    results.append({"id": item["id"], "subtype_csv": item["subtype"], "result": result,
                    "before": {"declared_reference": declared, "parser_reference": extracted, "finding": "PDF_017"},
                    "after": {"declared_reference": declared, "parser_comparison": result,
                              "canonical_contract_id": all_ids[0] if len(all_ids) == 1 else "TJK26T009266000EE" if item["id"] == 23793 else None},
                    "data_change_required_for_parser_recovery": False if result == "EQUIVALENZA_DOCUMENTATA" else None,
                    "data_changes_applied": False, "whole_instance_eligibility_assessed": False,
                    "detail": detail, "all_contract_ids": all_ids,
                    "visual_review_page": visual_pages.get(item["id"]),
                    "files_read": len(documents), "pages_read": len(pages),
                    "pages_without_text": [page["page"] for _, page in pages if not page["text_present"]],
                    "provenance": provenance})

output = {"source": "ref-groups.json + pdf-manifest.json", "scope": "98 sole PDF_017; nessuna modifica applicativa o SQL",
          "method": "pypdf, tutte le pagine dei documenti associati; verifica visiva di sei pagine rappresentative; nessun OCR eseguito",
          "counts": dict(Counter(item["result"] for item in results)), "items": results}
(ROOT / "ref-pdf-review.json").write_text(json.dumps(output, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(output["counts"], ensure_ascii=False))
print("unconfirmed_ids", [item["id"] for item in results if item["result"] != "EQUIVALENZA_DOCUMENTATA"])
