"""The document response cache lives directly on D:, never in LocalAppData."""
from pathlib import Path


RESPONSE_CACHE_DIRECTORY = Path("D:/DrosoDocLab/responses-v2")


def response_cache_directory():
    target = RESPONSE_CACHE_DIRECTORY.absolute()
    if not Path(target.anchor).is_dir():
        raise OSError("Disco D: non disponibile: cache risposte non utilizzabile. Nessun ripiego su C:.")
    if target.resolve() != target:
        raise OSError("La cache risposte deve essere una cartella diretta su D:, senza collegamenti ad altre cartelle.")
    target.mkdir(parents=True, exist_ok=True)
    if target.resolve() != target:
        raise OSError("Il percorso fisico della cache risposte è cambiato. Operazione interrotta.")
    return target


def resolve_response_directory(directory):
    path = Path(directory)
    # Protect direct callers that still pass the old LocalAppData directory.
    if path.name.casefold() == "responses-v2":
        return response_cache_directory()
    return path.resolve()
