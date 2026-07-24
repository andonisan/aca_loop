# Orquestador Agéntico de Backlog (nombre de trabajo)

> Borrador de propuesta de producto. Redactado a partir de una descripción
> verbal, ampliado con lo ya implementado en este repo como v1 (punto 6) y
> con el proceso de referencia documentado en el `CONTRIBUTING.md` de
> ds-connect (punto 4).
>
> **Estado (2026-07-24): implementado.** El motor de mapeo configurable, los
> tres adaptadores de vendor (GitHub, GitLab, Azure DevOps) y el despachador
> sobre .NET Aspire + Azure Container Apps ya existen en `src/` — ver
> `openspec/changes/agentic-backlog-orchestrator/` y el `README.md` del
> repo. Lo que falta: pilotar GitLab y Azure DevOps contra un proyecto real
> (nunca verificado en vivo) y el control-plane centralizado
> (`openspec/changes/orchestrator-portal/`).

## 1. Resumen ejecutivo

Una capa de abstracción sobre la parte de **backlog** de cualquier proveedor
(Azure DevOps, GitHub, GitLab) para el seguimiento de historias de usuario.
El producto detecta, de forma **configurable**, cuándo una historia de
usuario entra en cierto estado — usando el mecanismo nativo de cada vendor
(labels en GitHub, estados de work item en Azure DevOps, labels/estado de
issue en GitLab) — y dispara una serie de **acciones con IA** en respuesta a
esa transición de estado.

La ejecución ocurre en un contenedor Docker que trae **OpenCode, GitHub CLI
y Claude Code instalados a la vez**; por configuración se decide qué
runtime/instalación se usa en cada despliegue, sin que eso sea un fork
arquitectónico. Cada vez que una historia entra en el estado configurado, se
levanta un **"AI pod" sobre Azure Container Apps** (un job scale-to-zero, no
un pod de Kubernetes/AKS) que ejecuta esa implementación en modo *loop*, sin
intervención humana salvo que el propio workflow la requiera.

## 2. Problema que resuelve

Hoy el proceso de "grillado → propuesta → implementación → revisión" de
historias de usuario es automatizable por diseño (hay comandos internos como
`/propose` o `/implement`) pero no está automatizado: un humano tiene que
fijarse en la etiqueta/estado y lanzar el agente manualmente. Además, cada
equipo/cliente puede vivir en un vendor de backlog distinto (Azure DevOps,
GitHub, GitLab), y hoy cualquier automatización de este tipo se acopla a uno
solo de ellos.

## 3. Qué es el producto

- **Capa de abstracción de backlog**: una interfaz común de "historia de
  usuario + estado" por encima de Azure DevOps, GitHub y GitLab. Cada vendor
  aporta su propio mecanismo de notificación de cambio de estado (webhook de
  issues, webhook de work item, webhook de issues/labels de GitLab) y su
  propio mecanismo de "estado" (label vs. work item state vs. label/estado
  de issue); el producto normaliza esto a un modelo único.
- **Motor de estados configurable**: quién decide qué estado de la historia
  dispara qué acción de IA no es fijo — es configuración. Esto es lo que
  hoy, en la v1 de este mismo repo, está *hardcodeado* a dos labels de
  ds-connect (`status:ready-for-proposal`, `status:ready-for-implementation`)
  y sería el primer punto a generalizar.
- **Imagen de agente multi-runtime**: un Docker con OpenCode, `gh` (GitHub
  CLI) y Claude Code instalados los tres a la vez; por configuración se
  decide con cuál de las tres instalaciones se ejecuta cada despliegue/tarea.
- **Despachador sobre Azure Container Apps**: cada vez que una historia
  entra en el estado configurado se levanta un "AI pod" — un Container Apps
  Job scale-to-zero — que clona el repo, ejecuta la instrucción configurada
  para ese estado, y deja la historia en el siguiente estado del workflow.

## 4. Caso de uso de referencia (workflow con estados)

El workflow de referencia es el ciclo spec-first de ds-connect
(`CONTRIBUTING.md` de ese repo): nueve estados (`status:*` labels) con dos
**gates de máquina** y dos **revisiones humanas** (HITL) sobre un único
branch/PR por historia.

```
idea ─grill─▶ needs-refinement ─(resolver gaps)─▶ ready-for-proposal
   (gate DoR)                                          │
                                                 propose (gate LLM)
                                                       ▼
                                        proposal-review ─(humano valida)─▶ ready-for-implementation
                                        draft PR · HITL #1                     (gate LLM, WIP ≤ 4)
                                                                                      │
   done ◀─sync─ code-review ◀─implement─ in-progress ◀───────────────────────────────┘
                same PR · HITL #2
```

1. **Grill** (`/ds:grill`) — interroga la idea/issue contra la Definition of
   Ready; si no la cumple → `needs-refinement` con los gaps comentados; si
   la cumple → `ready-for-proposal`.
2. **Ready for proposal** — *gate de máquina*: al entrar en este estado
   salta el loop agéntico: se levanta el AI pod, clona el repo y ejecuta el
   comando interno `/ds:propose`. El agente crea la propuesta (OpenSpec
   change) en una rama, abre un **draft PR** y deja la historia en
   `proposal-review`.
3. **Proposal review** (HITL #1) — un humano revisa/corrige la spec en el
   draft PR. Si pide cambios y re-aplica el gate label, el agente se
   re-despacha y revisa usando los comentarios del PR. Si valida, mueve la
   historia a `ready-for-implementation`.
4. **Ready for implementation** — *gate de máquina* (con límite WIP de 4
   concurrentes): salta de nuevo el AI pod, que ejecuta `/ds:implement`
   sobre la **misma rama/PR**, marca el PR como ready y deja la historia en
   `code-review`.
5. **Code review** (HITL #2) — revisión humana del código en el mismo PR;
   igual que en HITL #1, "request changes" + re-label re-despacha al agente.
6. **Sync** (`/ds:sync`, humano) — cierre: retro, archivo de specs,
   squash-merge de un commit limpio a main → `done`.

Además existe `status:blocked`, alcanzable desde cualquier estado — es donde
el agente deja la historia si una ejecución falla, con un comentario del
contexto del fallo.

La generalización de producto: estos estados, gates y comandos son la
configuración de *un* cliente/repo. El producto permite definir el mapeo
"estado → acción agéntica" para cualquier workflow equivalente en cualquier
vendor.

## 5. Arquitectura (propuesta, a validar)

```
Vendor de backlog (Azure DevOps | GitHub | GitLab)
        │  evento nativo (webhook: work item updated / issue labeled / issue estado)
        ▼
Adaptador de vendor  (uno por proveedor)
  - normaliza el evento a {repo, item_id, estado_canónico}
  - aplica el mapeo configurable "estado nativo → estado canónico → acción"
        │
        ▼
Cola de trabajo  (Storage Queue, como en la v1 actual)
        │
        ▼
Azure Container Apps Job ("ai pod", KEDA scale 0..N por profundidad de cola)
  - una ejecución = un "AI pod" por historia en el estado configurado
  - selecciona runtime del agente (OpenCode | Claude Code | gh) por config
  - clona el repo, ejecuta la instrucción del estado, actualiza el estado
    de la historia en el vendor de origen al terminar
```

## 6. Relación con lo ya implementado en este repo

Este mismo repositorio (`aca_loop`) implementaba una **v1 de este producto,
acoplada a un único vendor y a un único cliente** (ver
[`openspec/changes/ai-loop-aca-job/`](../../openspec/changes/ai-loop-aca-job/),
aún sin archivar) y desde entonces se ha generalizado al producto completo
descrito arriba: ver [`README.md`](../../README.md) y
[`openspec/changes/agentic-backlog-orchestrator/`](../../openspec/changes/agentic-backlog-orchestrator/).

| Pieza | v1 (solo GitHub / ds-connect) | Estado actual |
|---|---|---|
| Detección de estado | Webhook de GitHub `issues`, dos labels hardcodeadas | Adaptador por vendor (`src/modules/{GitHub,GitLab,AzureDevOps}/`) + motor de mapeo YAML validado en arranque |
| Despacho | Azure Container Apps Job + KEDA sobre Storage Queue | Mismo patrón, generalizado a multi-vendor sobre .NET Aspire (`src/root/Orchestrator.{Receiver,Agent}`) |
| Imagen de agente | Node: git, `gh`, Claude Code y opencode en la misma imagen | .NET + los mismos binarios; runtime seleccionado **por estado** vía el mapping, no fijo por despliegue |
| Comandos | `/ds:propose`/`/ds:implement`, fijos | Instrucción por estado, configurable por cliente/repo en `mapping.yaml` |
| Vendors soportados | Solo GitHub | GitHub (probado en producción), GitLab y Azure DevOps (implementados, con tests, **sin pilotar en vivo**) |

Lo que queda pendiente, honesto: GitLab y Azure DevOps no se han probado
contra un proyecto real (hace falta una cuenta/credenciales reales que no
había disponibles al implementarlos); Azure DevOps usa PAT en vez de service
principal de Entra por la misma razón (ver design.md). El control-plane
centralizado para operar múltiples stacks de cliente a la vez es un
producto aparte, ya propuesto: `openspec/changes/orchestrator-portal/`.

## 7. Preguntas abiertas (resueltas)

- ~~¿Un entorno ACA para todos los clientes, o uno por cliente?~~ **Resuelto:
  un stack por cliente** — blast radius independiente; el control-plane
  centralizado (`orchestrator-portal`) da la vista cross-cliente por
  encima, sin reabrir esta decisión.
- ~~¿Mapeo de estados por repo o como recurso central?~~ **Resuelto: se
  despliega junto al orquestador** (fichero YAML horneado en la imagen del
  Receiver), nunca leído del repo target — es superficie de autorización,
  no debe ser editable por quien puede hacer push al repo.
- ~~¿Qué pasa con el estado en el vendor si el Job falla?~~ **Resuelto:**
  cada mapping declara `onFailureState` por estado; GitHub/GitLab
  intercambian el label `status:*`, Azure DevOps escribe el campo
  `System.State` directamente (ya no necesita el invariante de "un solo
  label" porque el estado es de por sí univaluado).
