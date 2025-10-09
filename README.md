# Texture Set To Material (UDIM) — Unity Editor Tool

**Resumo**  
Editor script para Unity que varre uma pasta de texturas com UDIMs (1001,1002,...) e cria materiais **Universal Render Pipeline/Lit** nomeados apenas pelo UDIM (`1001.mat`, `1002.mat`, ...). Mapeia **BaseColor, Normal, Height, Emission e Metallic**. Ignora arquivos com `roughness`.

## Recursos
- Agrupa por UDIM (detecta `_1001` ou `.1001` no nome).
- Cria materiais URP/Lit (padrão) ou usa um *Template Material*.
- Ativa emission automaticamente quando há mapa de emissive.
- Reimporta mapas normais como NormalMap (opcional).
- Opção para sobrescrever materiais existentes.

## Requisitos
- Unity Editor (versão compatível com URP se quiser URP/Lit).
- Projeto configurado com URP se quiser os resultados ideais.
- Coloque o script na pasta `Assets/Editor/`.

## Instalação
1. Crie (se necessário) a pasta `Assets/Editor/`.
2. Copie `TextureSetToMaterial_UDIM.cs` para `Assets/Editor/`.
3. Volte ao Unity e aguarde a compilação.

## Uso
1. Menu: **Window → Texture Set To Material (UDIM)**.  
2. Em **Input Folder**, selecione a pasta com as texturas (ex.: `OXIGENIO_BaseColor_1001.png`, `OXIGENIO_Normal_1001.png`, etc.).  
3. Em **Output Folder**, selecione a pasta onde quer salvar os materiais.  
4. (Opcional) Arraste um *Template Material* com shader customizado se não quiser o padrão URP/Lit.  
5. Ajuste `Overwrite existing` e `Force normal map importer` conforme desejar.  
6. Clique **Create UDIM Materials**.

## Convenções de nomes suportadas
O script reconhece UDIMs em formatos comuns:
- `NOME_BaseColor_1001.png`
- `NOME.BaseColor.1001.exr`
- Remove `_UV_UV` automaticamente se presente.

Map types detectados (case-insensitive, sufixos esperados):  
- Albedo/BaseColor: `base`, `albedo`, `diffuse`  
- Normal: `normal`, `n`, `norm`  
- Height: `height`, `disp`, `depth`  
- Emission: `emiss`, `emit`  
- Metallic: `metal`, `metallic`, `metalness`

Arquivos que contenham **`roughness`** são ignorados (por pedido).

## Limitações & Observações
- **Nome dos materiais**: por design os materiais são nomeados só pelo UDIM (`1001.mat`). Se houver texturas de diferentes assets com o mesmo UDIM (ex.: `OX_1001` e `OUTRO_1001`), elas serão combinadas no mesmo material `1001.mat`. Se quiser diferenciar automaticamente (ex.: `1001_1.mat`) posso adicionar essa lógica.  
- **Roughness**: é ignorado — se precisar que roughness seja tratado (inverter, pack em MaskMap), isso exige rotina extra.  
- **Packing avançado** (ex.: criar MaskMap URP juntando metallic/occlusion/roughness) não está implementado por padrão, mas pode ser adicionado.  
- **URP obrigatório para shader URP/Lit**: o script tenta `Shader.Find("Universal Render Pipeline/Lit")`. Se não achar, cai para `Standard` e avisa no Console.

