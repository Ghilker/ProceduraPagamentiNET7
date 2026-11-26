#!/usr/bin/env bash
set -euo pipefail
umask 077

for tool in curl sha256sum flock; do
    if ! command -v "$tool" >/dev/null; then
        echo "Comando $tool mancante nella distribuzione Ubuntu." >&2
        exit 1
    fi
done

if ! command -v cc >/dev/null || ! command -v c++ >/dev/null; then
    echo 'Compilatori C/C++ mancanti in Ubuntu. Installare build-essential.' >&2
    exit 43
fi
if ! command -v ninja >/dev/null; then
    echo 'Ninja mancante in Ubuntu. Installare ninja-build.' >&2
    exit 44
fi

if ! command -v nvidia-smi >/dev/null && [[ -x /usr/lib/wsl/lib/nvidia-smi ]]; then
    export PATH="/usr/lib/wsl/lib:$PATH"
fi
if ! nvidia-smi --query-gpu=name,memory.total --format=csv,noheader; then
    echo 'GPU NVIDIA non disponibile in Ubuntu. Verificare WSL 2 e il driver NVIDIA su Windows.' >&2
    exit 1
fi

install_dir="$HOME/.local/share/procedurenet7/unlimited-ocr2"
mkdir -p "$install_dir"
exec 9>"$install_dir/server.lock"
if ! flock -n 9; then
    echo 'Unlimited-OCR 2 è in esecuzione oppure è già in corso una configurazione.' >&2
    exit 75
fi

cd "$install_dir"
trap 'echo "Configurazione Unlimited-OCR 2 interrotta alla riga $LINENO." >&2' ERR
export UV_CACHE_DIR="$install_dir/uv-cache"
export UV_PYTHON_INSTALL_DIR="$install_dir/python"
export HF_HOME="$install_dir/huggingface"

if [[ ! -x "$install_dir/bin/uv" ]]; then
    curl -fsSL --retry 2 https://astral.sh/uv/0.13.9/install.sh -o install-uv.sh
    UV_UNMANAGED_INSTALL="$install_dir/bin" sh install-uv.sh
fi
uv="$install_dir/bin/uv"
if [[ ! -x .venv/bin/python ]]; then
    "$uv" venv --python 3.10 .venv
fi

# Baidu's Git LFS wheel and digest from commit f0fa5f4c67fc5c258b698fbc690310375f53b29d.
wheel='sglang-0.5.20.dev13+g75b9b06b02-py3-none-any.whl'
wheel_hash='576c71dab3cacad843f6870739494e13b2e8169a69cc038f84395d6503e801ea'
if [[ ! -f "$wheel" ]] || ! printf '%s  %s\n' "$wheel_hash" "$wheel" | sha256sum --check --status; then
    curl -fLSs --retry 2 -o "$wheel" "https://media.githubusercontent.com/media/baidu/Unlimited-OCR/f0fa5f4c67fc5c258b698fbc690310375f53b29d/wheel/$wheel"
    printf '%s  %s\n' "$wheel_hash" "$wheel" | sha256sum --check
fi
"$uv" pip install --python .venv/bin/python "./$wheel" 'fastokens==0.3.2'

.venv/bin/python - <<'PY'
import torch
from huggingface_hub import snapshot_download

if not torch.cuda.is_available():
    raise SystemExit('CUDA non disponibile nel runtime PyTorch di Unlimited-OCR 2.')

print('Download del modello Unlimited-OCR 2 da Hugging Face.', flush=True)
snapshot_download(
    'baidu/Unlimited-OCR-2',
    revision='04cc37bbccb7fadb220b0821caa6dd86eb3499be',
    allow_patterns=['*.json', '*.py', '*.safetensors'],
    local_dir='model',
)
PY

cat > start.sh.tmp <<'BASH'
#!/usr/bin/env bash
set -euo pipefail
umask 077
cd -- "$(dirname -- "$0")"
exec 9>server.lock
flock -n 9 || exit 75
exec >server.log 2>&1

export HF_HUB_OFFLINE=1
export SGLANG_UOCR_EANABLE_DOC_LIMIT=1
export SGLANG_UOCR_DOC_GUNDAM_PAGE_WINDOW=10
export SGLANG_UOCR_DOC_BASE_PAGE_WINDOW=10
export SGLANG_UOCR_MAX_PAGE_TOKENS=20000
export SGLANG_OPT_UOCR_SAM_REL_POS_BIAS_MB=64
export SGLANG_UOCR_SHM_WRAP_MODEL_DATA=1
export SGLANG_UOCR_SHM_WRAP_MIN_MB=1
export SGLANG_VLM_CACHE_SIZE_MB=0
export PYTORCH_CUDA_ALLOC_CONF=expandable_segments:True

exec .venv/bin/python -m sglang.launch_server \
    --model-path "$PWD/model" \
    --served-model-name Unlimited-OCR-2 \
    --chat-template unlimited-ocr \
    --tokenizer-backend fastokens \
    --mem-fraction-static 0.7 \
    --max-running-requests 4 \
    --max-prefill-tokens 2048 \
    --enable-streaming-session \
    --disable-radix-cache \
    --mm-preprocess-cache-size-mb 0 \
    --cuda-graph-backend-decode full --cuda-graph-max-bs-decode 4 \
    --cuda-graph-backend-prefill full --cuda-graph-max-bs-prefill 16384 \
    --cuda-graph-bs-prefill 1024 2048 \
    --cuda-graph-config '{"prefill":{"backend":"full","full_prefill_max_req":4}}' \
    --host 127.0.0.1 --port 10000
BASH
chmod 700 start.sh.tmp
mv -f -- start.sh.tmp start.sh
echo 'Unlimited-OCR 2 configurato. Il servizio verrà avviato dalla lavorazione automatica quando si seleziona Unlimited-OCR 2.'
