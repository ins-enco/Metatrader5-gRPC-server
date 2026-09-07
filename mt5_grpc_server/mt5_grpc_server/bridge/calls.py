"""Which MetaTrader5 entry points the bridge will forward.

The bridge port is loopback-only, but an allowlist keeps a buggy or hostile
peer from reaching arbitrary attributes of the MetaTrader5 module.

``IDEMPOTENT_CALLS`` is the subset that may be retried after a transport
failure. Anything that changes state - logging in, sending an order, taking a
market book subscription - is deliberately absent: when a send fails there is
no way to know whether the worker already executed it, and replaying
``order_send`` would place a second order.
"""

IDEMPOTENT_CALLS = frozenset({
    "last_error",
    "version",
    "account_info",
    "terminal_info",
    "symbols_get",
    "symbols_total",
    "symbol_info",
    "symbol_info_tick",
    "copy_rates_from",
    "copy_rates_from_pos",
    "copy_rates_range",
    "copy_ticks_from",
    "copy_ticks_range",
    "market_book_get",
    "orders_get",
    "orders_total",
    "order_calc_margin",
    "order_calc_profit",
    "order_check",
    "positions_get",
    "positions_total",
    "history_orders_get",
    "history_orders_total",
    "history_deals_get",
    "history_deals_total",
})

MUTATING_CALLS = frozenset({
    "initialize",
    "login",
    "shutdown",
    "symbol_select",
    "market_book_add",
    "market_book_release",
    "order_send",
})

ALLOWED_CALLS = IDEMPOTENT_CALLS | MUTATING_CALLS
